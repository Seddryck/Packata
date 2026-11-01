using System.Data;
using System.Globalization;
using System.Numerics;
using Packata.Core.Contracts;

namespace Packata.Core;

// One interpretation owns the source parameters, diagnostics, and independent projections.
internal sealed class FieldTypeResolutionContext
{
    private static readonly string[] TemporalTypes = ["date", "time", "datetime", "timestamp"];
    private static readonly string[] StringAnnotations =
        ["password", "byte", "binary", "email", "uuid", "uri", "hostname", "ipv4", "ipv6", "xml"];
    private static readonly string[] IntegerBounds = ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum"];
    private readonly DataField _field;
    private readonly TypeMappingOptions _options;
    private readonly string _path;
    private readonly string? _type;
    private readonly string? _format;
    private readonly string? _semanticFormat;
    private readonly bool _temporal;
    private readonly List<TypeMappingDiagnostic> _diagnostics = [];
    private TypeMappingCatalog.Entry? _entry;
    private TypeMappingCatalog.Entry? _defaultEntry;
    private Type? _runtime;
    private DbType? _database;
    private int? _precision;
    private int? _scale;
    private int? _width;
    private bool? _signed;
    private bool? _timezone;
    private string? _zone;
    private TemporalSemantics? _semantics;
    private bool _unknownFormat;
    private FieldTypeResolution? _items;
    private FieldTypeResolution[] _children = [];

    internal FieldTypeResolutionContext(DataField field, TypeMappingOptions options, string path)
    {
        _field = field;
        _options = options;
        _path = path;
        _type = field.LogicalType?.Trim().ToLowerInvariant();
        if (_type == "decimal") _type = "number";
        _format = field.Format?.Trim();
        _semanticFormat = _format?.ToLowerInvariant();
        _temporal = TemporalTypes.Contains(_type);
    }

    internal FieldTypeResolution Resolve()
    {
        ReadParameters();
        SelectEntry();
        SelectIntegerEntry();
        _runtime = _entry?.RuntimeType;
        _database = _entry?.DatabaseType;
        ProjectTemporal();
        ProjectNumber();
        CheckIntegerBounds();
        ResolveNestedFields();
        ProjectShape();
        RejectSourceErrors();
        ApplyFallback();
        return ApplyLegacyRepresentation(CreateResolution());
    }

    private void Report(string code, TypeMappingTarget target, string message,
        TypeMappingSeverity severity = TypeMappingSeverity.Error)
        => _diagnostics.Add(new(code, severity, target, _path, _field.LogicalType,
            _field.Format, _field.PhysicalType, message));

    private object? Parameter(string name)
        => _field.Constraints.FirstOrDefault(c => c.Kind == name)?.Value;

    private int? Integer(string name)
    {
        var value = Parameter(name);
        if (value is null) return null;
        if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
        Report("InvalidParameter", TypeMappingTarget.Source, $"'{name}' must be an integer.");
        return null;
    }

    private bool? Boolean(string name)
    {
        var value = Parameter(name);
        if (value is null) return null;
        if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var result)) return result;
        Report("InvalidParameter", TypeMappingTarget.Source, $"'{name}' must be a boolean.");
        return null;
    }

    private void ReadParameters()
    {
        _precision = Integer("precision");
        _scale = Integer("scale");
        _width = Integer("integerWidth");
        _signed = Boolean("signed");
        ReadIntegerFormat();
        _timezone = Boolean("timezone");
        _zone = Parameter("defaultTimezone") as string;
        CheckIntegerParameters();
        CheckDecimalParameters();
        CheckTimezoneParameters();
        ReadTemporalSemantics();
    }

    private void ReadIntegerFormat()
    {
        if (_type != "integer" || _semanticFormat is not { Length: > 1 } format) return;
        if (format[0] is not ('i' or 'u')) return;
        if (!int.TryParse(format[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var width)) return;
        SetIntegerFormat(width, format[0] == 'i');
    }

    private void SetIntegerFormat(int width, bool? signed)
    {
        if ((_width is not null && _width != width) || (_signed is not null && _signed != signed))
        {
            Report("ConflictingSemantics", TypeMappingTarget.Source,
                "Integer format takes precedence over integerWidth/signed parameters.", TypeMappingSeverity.Warning);
        }

        _width = width;
        _signed = signed;
    }

    private void CheckIntegerParameters()
    {
        if (_type != "integer" && (_width is not null || _signed is not null))
            Report("UnsupportedParameter", TypeMappingTarget.Source, "Integer parameters require logical type integer.");
    }

    private void CheckDecimalParameters()
    {
        if ((_precision is not null && _type != "number" && !_temporal) || (_scale is not null && _type != "number"))
            Report("UnsupportedParameter", TypeMappingTarget.Source, "Precision/scale parameters are unsupported for this logical type.");
    }

    private void CheckTimezoneParameters()
    {
        if (!_temporal && (_timezone is not null || _zone is not null))
            Report("UnsupportedParameter", TypeMappingTarget.Source, "Timezone parameters require a temporal logical type.");
        if (Parameter("defaultTimezone") is not null && _zone is null)
            Report("InvalidParameter", TypeMappingTarget.Source, "defaultTimezone must be a string.");
    }

    private void ReadTemporalSemantics()
    {
        if (!_temporal) return;
        _semantics = _type == "timestamp" ? TemporalSemantics.Instant : TemporalSemantics.Local;
        if (_timezone is not null)
            _semantics = _timezone.Value ? TemporalSemantics.Offset : TemporalSemantics.Local;
        if (!DateTimeFormatConverter.HasOffset(_format)) return;
        if (_timezone == false)
        {
            Report("ConflictingSemantics", TypeMappingTarget.Source,
                "Offset parsing pattern conflicts with timezone=false; the parsing pattern takes precedence.", TypeMappingSeverity.Warning);
        }

        _semantics = TemporalSemantics.Offset;
    }

    private void SelectEntry()
    {
        _entry = TypeMappingCatalog.Entries.FirstOrDefault(e => e.LogicalType == _type && e.Format == _semanticFormat);
        _defaultEntry = TypeMappingCatalog.Entries.FirstOrDefault(e => e.LogicalType == _type && e.Format is null);
        _unknownFormat = _entry is null && !IsAnnotation();
        if (_unknownFormat)
            Report("UnsupportedFormat", TypeMappingTarget.Source, $"Format '{_format}' has no registered representation.");
        if (_entry is null && !_unknownFormat) _entry = _defaultEntry;
    }

    private bool IsAnnotation()
    {
        if (_semanticFormat is null or "default" or "any") return true;
        if (_type == "string") return StringAnnotations.Contains(_semanticFormat);
        if (_type == "number") return _semanticFormat == "decimal";
        return _temporal && _format is not null && IsPattern(_format);
    }

    private static bool IsPattern(string format)
    {
        if (!format.Contains('%')) return false;
        try { DateTimeFormatConverter.Convert(format); return true; }
        catch (NotSupportedException) { return false; }
    }

    private void SelectIntegerEntry()
    {
        if (_type != "integer") return;
        if (_entry?.IntegerWidth is { } width)
        {
            SetIntegerFormat(width, _entry.Signed);
            return;
        }
        if (_unknownFormat || (_width is null && _signed is null)) return;
        // Do not invent a missing width or signedness.
        _entry = TypeMappingCatalog.Entries.FirstOrDefault(e => e.LogicalType == _type
            && e.IntegerWidth == _width && e.Signed == _signed && _width is not null && _signed is not null);
        if (_entry is null)
        {
            Report("UnsupportedIntegerDomain", TypeMappingTarget.Source,
                "Integer width and signedness must declare a supported complete domain.");
        }
    }

    private void ProjectTemporal()
    {
        if (!_temporal || _entry is null) return;
        if (_type is "datetime" or "timestamp")
        {
            _runtime = _semantics == TemporalSemantics.Local ? typeof(DateTime) : typeof(DateTimeOffset);
            _database = _semantics == TemporalSemantics.Local ? DbType.DateTime : DbType.DateTimeOffset;
        }
        CheckTemporalOffset();
        CheckTemporalPrecision();
        if (_zone is not null)
        {
            Report("RepresentationLoss", TypeMappingTarget.Runtime,
                "CLR temporal values do not retain a named timezone; it remains available in metadata.", TypeMappingSeverity.Warning);
        }
    }

    private void CheckTemporalOffset()
    {
        if (_type is "time" or "date" && _semantics != TemporalSemantics.Local)
        {
            _runtime = null;
            _database = null;
            Report("UnsupportedTemporalSemantics", TypeMappingTarget.Source, "DateOnly/TimeOnly and DbType.Date/Time cannot retain an offset.");
        }
    }

    private void CheckTemporalPrecision()
    {
        if (_precision < 0)
            Report("InvalidParameter", TypeMappingTarget.Source, "Temporal precision cannot be negative.");
        if (_type == "date" && _precision is not null)
            Report("UnsupportedParameter", TypeMappingTarget.Source, "Date has no fractional second precision.");
        if (_precision > 7)
        {
            _runtime = null;
            Report("RepresentationLoss", TypeMappingTarget.Runtime, "CLR temporal types retain at most seven fractional second digits.");
        }
    }

    private void ProjectNumber()
    {
        if (_type != "number") return;
        if (_precision <= 0 || _scale < 0 || (_precision is not null && _scale > _precision))
            Report("InvalidParameter", TypeMappingTarget.Source, "Decimal precision must be positive and scale must be between zero and precision.");
        CheckDecimalCapacity();
        CheckFloatingPointPrecision();
    }

    private void CheckDecimalCapacity()
    {
        if (_runtime == typeof(decimal) && (_precision > 28 || _scale > 28))
        {
            _runtime = null;
            Report("RepresentationLoss", TypeMappingTarget.Runtime,
                "The full declared decimal domain does not fit System.Decimal (at most 28 guaranteed decimal digits and scale 0..28).");
        }
    }

    private void CheckFloatingPointPrecision()
    {
        if (_runtime != typeof(float) && _runtime != typeof(double)) return;
        if (_precision is not null || _scale is not null)
        {
            Report("RepresentationLoss", TypeMappingTarget.Source,
                "Binary floating point cannot represent a declared fixed decimal domain exactly.");
        }
    }

    private void CheckIntegerBounds()
    {
        if (_type != "integer" || _entry is null) return;
        // With no explicit format, the historical integer representation is Int32.
        var bits = _entry.IntegerWidth ?? 32;
        var signed = _entry.Signed ?? true;
        var min = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
        var max = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
        foreach (var constraint in _field.Constraints.Where(c => IntegerBounds.Contains(c.Kind)))
            CheckIntegerBound(constraint, min, max);
    }

    private void CheckIntegerBound(DataConstraint constraint, BigInteger min, BigInteger max)
    {
        if (!BigInteger.TryParse(Convert.ToString(constraint.Value, CultureInfo.InvariantCulture),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var bound))
        {
            Report("InvalidParameter", TypeMappingTarget.Source, $"'{constraint.Kind}' must be an integer bound.");
            return;
        }
        if (constraint.Kind == "exclusiveMinimum") bound++;
        if (constraint.Kind == "exclusiveMaximum") bound--;
        if (bound < min || bound > max)
            Report("RepresentationLoss", TypeMappingTarget.Source, "Declared integer bounds exceed the selected integer representation.");
    }

    private void ResolveNestedFields()
    {
        _items = _field.Items is null ? null
            : new FieldTypeResolutionContext(_field.Items, _options, _path + "/items").Resolve();
        _children = _field.Children.Select(child =>
            new FieldTypeResolutionContext(child, _options, _path + "/" + child.Name).Resolve()).ToArray();
    }

    private void ProjectShape()
    {
        if (_type == "array" && _items is not null && _field.Children.Count == 0)
        {
            _runtime = _items.RuntimeType?.MakeArrayType();
            Report("UnsupportedShape", TypeMappingTarget.Database, "DbType has no portable array representation.");
            return;
        }
        if (_type == "object")
        {
            // Explicit object is opaque, distinct from an unknown-type fallback.
            if (_field.Items is not null)
                Report("UnsupportedShape", TypeMappingTarget.Source, "Object items do not define a canonical map key/value shape.");
            return;
        }
        CheckUnsupportedShape();
    }

    private void CheckUnsupportedShape()
    {
        if (_type == "array" || _field.Items is not null || _children.Length > 0)
            Report("UnsupportedShape", TypeMappingTarget.Source, "Nested shape is inconsistent or has no portable representation.");
        else if (_defaultEntry is null)
            Report("UnsupportedType", TypeMappingTarget.Source, $"Logical type '{_field.LogicalType}' is unsupported.");
    }

    private void RejectSourceErrors()
    {
        if (!_diagnostics.Any(d => d.Severity == TypeMappingSeverity.Error && d.Target == TypeMappingTarget.Source)) return;
        _runtime = null;
        _database = null;
    }

    private void ApplyFallback()
    {
        if (_options.Fallback == TypeMappingFallback.Reject) return;
        if (_runtime is null) ApplyRuntimeFallback();
        if (_database is null) ApplyDatabaseFallback();
    }

    private void ApplyRuntimeFallback()
    {
        _runtime = _options.Fallback switch
        {
            TypeMappingFallback.String => typeof(string),
            TypeMappingFallback.LogicalType => _defaultEntry?.RuntimeType ?? typeof(object),
            _ => typeof(object)
        };
        Report("Fallback", TypeMappingTarget.Runtime, $"Explicit fallback selected {_runtime.Name}.", TypeMappingSeverity.Warning);
    }

    private void ApplyDatabaseFallback()
    {
        _database = _options.Fallback switch
        {
            TypeMappingFallback.String => DbType.String,
            TypeMappingFallback.LogicalType => _defaultEntry?.DatabaseType ?? DbType.Object,
            _ => DbType.Object
        };
        Report("Fallback", TypeMappingTarget.Database, $"Explicit fallback selected {_database}.", TypeMappingSeverity.Warning);
    }

    private FieldTypeResolution CreateResolution()
    {
        _diagnostics.AddRange(_items?.Diagnostics ?? []);
        _diagnostics.AddRange(_children.SelectMany(child => child.Diagnostics));
        return new(_field, _path, _type, _field.Format,
            new(_width, _signed, _temporal ? null : _precision, _scale, _temporal ? _precision : null, _semantics, _zone),
            _runtime, _database, _items, Array.AsReadOnly(_children), _diagnostics.AsReadOnly());
    }

    private FieldTypeResolution ApplyLegacyRepresentation(FieldTypeResolution result)
    {
        if (!_options.PreserveLegacyRepresentation) return result;
        var legacy = SelectLegacyEntry(out var offset);
        var runtime = offset ? typeof(DateTimeOffset) : legacy?.RuntimeType ?? typeof(object);
        var database = offset ? DbType.DateTimeOffset : legacy?.DatabaseType ?? DbType.Object;
        if (result.RuntimeType != runtime) result = result.WithRuntime(runtime, "LegacyRepresentation");
        if (result.DatabaseType != database) result = result.WithDatabase(database, "LegacyRepresentation");
        return result;
    }

    private TypeMappingCatalog.Entry? SelectLegacyEntry(out bool offset)
    {
        var legacy = TypeMappingCatalog.Entries.FirstOrDefault(e => TypeMappingCatalog.IsLegacyEntry(e)
            && e.LogicalType == _field.LogicalType && e.Format == _field.Format);
        offset = legacy is null && TypeMappingCatalog.HasOffset(_field.LogicalType ?? "", _field.Format);
        return legacy ?? TypeMappingCatalog.Entries.FirstOrDefault(e => e.LogicalType == _field.LogicalType && e.Format is null);
    }
}
