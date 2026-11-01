using System.Data;
using Apache.Arrow;
using Apache.Arrow.Types;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Arrow;

public sealed record ArrowDiagnostic(string Code, string Path, string Message, bool IsError = false);
public sealed record ArrowSchemaResult(Schema? Schema, IReadOnlyList<ArrowDiagnostic> Diagnostics);

/// <summary>One field-aware projection shared by code generation and IPC consumers.</summary>
public static class ArrowSchemaProjection
{
    private static readonly IReadOnlyDictionary<DbType, IArrowType> Scalars = new Dictionary<DbType, IArrowType>
    {
        [DbType.SByte] = Int8Type.Default, [DbType.Byte] = UInt8Type.Default,
        [DbType.Int16] = Int16Type.Default, [DbType.UInt16] = UInt16Type.Default,
        [DbType.Int32] = Int32Type.Default, [DbType.UInt32] = UInt32Type.Default,
        [DbType.Int64] = Int64Type.Default, [DbType.UInt64] = UInt64Type.Default,
        [DbType.Single] = FloatType.Default, [DbType.Double] = DoubleType.Default,
        [DbType.String] = StringType.Default, [DbType.Boolean] = BooleanType.Default, [DbType.Date] = Date32Type.Default
    };

    public static ArrowSchemaResult Map(DataSchema schema)
    {
        var diagnostics = new List<ArrowDiagnostic>();
        var fields = schema.Fields.Select(f => Field(FieldTypeResolver.Resolve(f, new(SourcePath: f.Name)), diagnostics)).ToArray();
        diagnostics.Add(new("ARROW_CONSTRAINTS", "$", "Arrow nullability describes values; field presence, uniqueness, quality rules and other contract constraints require separate validation."));
        return new(diagnostics.Any(d => d.IsError) ? null : new Schema(fields!, Metadata(schema.Extensions)), diagnostics);
    }

    private static Field? Field(FieldTypeResolution field, List<ArrowDiagnostic> diagnostics)
    {
        foreach (var d in field.Diagnostics.Where(d => d.Target == TypeMappingTarget.Source && d.FieldPath == field.FieldPath))
            diagnostics.Add(new(d.Code, d.FieldPath, d.Message, d.Severity == TypeMappingSeverity.Error));
        var type = Type(field, diagnostics);
        if (type is null)
        {
            diagnostics.Add(new("ARROW_TYPE", field.FieldPath, "The field has no lossless supported Arrow representation. Dictionary and extension types require an explicit adapter and are not inferred.", true));
            return null;
        }
        var metadata = Metadata(field.Field.Extensions);
        metadata["packata.required"] = field.Required ? "true" : "false";
        if (field.Field.Description is { } description) metadata["description"] = description;
        return new(field.Field.Name, type, field.Nullable, metadata);
    }

    private static IArrowType? Type(FieldTypeResolution f, List<ArrowDiagnostic> diagnostics)
    {
        if (f.Field.Extensions.TryGetNamespace("arrow", out var options) && options.TryGetValue("type", out var representation))
            return Map(f, representation, diagnostics);
        if (f.LogicalType == "array") return List(f, diagnostics);
        if (f.LogicalType == "object") return Struct(f, diagnostics);
        return Scalar(f, diagnostics);
    }

    private static IArrowType? List(FieldTypeResolution f, List<ArrowDiagnostic> diagnostics)
        => f.Items is not null && Field(f.Items, diagnostics) is { } item ? new ListType(item) : null;

    private static IArrowType? Struct(FieldTypeResolution f, List<ArrowDiagnostic> diagnostics)
    {
        if (f.Children.Count == 0) return null;
        var fields = f.Children.Select(c => Field(c, diagnostics)).ToArray();
        return fields.All(c => c is not null) ? new StructType(fields!) : null;
    }

    private static IArrowType? Scalar(FieldTypeResolution f, List<ArrowDiagnostic> diagnostics)
    {
        // Binary is an explicit annotation on the canonical string type.
        if (f.LogicalType == "string" && f.SourceFormat == "binary") return BinaryType.Default;
        if (f.DatabaseType is { } database && Scalars.TryGetValue(database, out var scalar)) return scalar;
        return ParameterizedType(f, diagnostics);
    }

    private static IArrowType? Map(FieldTypeResolution f, object? representation, List<ArrowDiagnostic> diagnostics)
    {
        if (representation is not "map" || f.Children.Count != 2) return null;
        var key = Field(f.Children[0], diagnostics);
        var value = Field(f.Children[1], diagnostics);
        if (key is null || value is null || key.IsNullable) return null;
        return new MapType(key, value);
    }

    private static IArrowType? ParameterizedType(FieldTypeResolution f, List<ArrowDiagnostic> diagnostics)
    {
        return f.DatabaseType switch
        {
            DbType.Decimal => Decimal(f.Parameters),
            DbType.Time when f.Parameters.TemporalPrecision is null or <= 9 => Time(f.Parameters.TemporalPrecision),
            DbType.DateTime or DbType.DateTimeOffset => Timestamp(f, diagnostics),
            _ => null
        };
    }

    private static IArrowType? Decimal(TypeParameters parameters)
    {
        if (parameters.Scale is not >= 0) return null;
        return parameters.Precision switch
        {
            > 0 and <= 38 => new Decimal128Type(parameters.Precision.Value, parameters.Scale.Value),
            > 38 and <= 76 => new Decimal256Type(parameters.Precision.Value, parameters.Scale.Value),
            _ => null
        };
    }

    private static IArrowType Time(int? precision) => precision switch
    {
        0 => new Time32Type(TimeUnit.Second), <= 3 => new Time32Type(TimeUnit.Millisecond),
        <= 6 => new Time64Type(TimeUnit.Microsecond), _ => new Time64Type(TimeUnit.Nanosecond)
    };

    private static IArrowType? Timestamp(FieldTypeResolution f, List<ArrowDiagnostic> diagnostics)
    {
        if (f.Parameters.TemporalPrecision > 9) return null;
        var zone = f.Parameters.Timezone;
        if (f.Parameters.TemporalSemantics == TemporalSemantics.Offset && zone is null) return null;
        if (f.Parameters.TemporalSemantics == TemporalSemantics.Instant) zone ??= "UTC";
        var unit = f.Parameters.TemporalPrecision switch
        { 0 => TimeUnit.Second, <= 3 => TimeUnit.Millisecond, <= 6 => TimeUnit.Microsecond, _ => TimeUnit.Nanosecond };
        diagnostics.Add(new("ARROW_TEMPORAL_RANGE", f.FieldPath, "Arrow timestamps use signed 64-bit epoch units; validate calendar range and precision when converting values. Per-row offsets are not retained."));
        return new TimestampType(unit, zone);
    }

    private static Dictionary<string, string> Metadata(ExtensionMetadata extensions)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (extensions.TryGetNamespace("arrow", out var values))
        {
            foreach (var (key, value) in values)
            {
                if (key.StartsWith("ARROW:extension:", StringComparison.Ordinal))
                    throw new NotSupportedException("Arrow extension types require registration and a dedicated adapter.");
                if (value is not string text) throw new NotSupportedException("Arrow metadata values must be strings.");
                result.Add(key, text);
            }
        }

        return result;
    }
}
