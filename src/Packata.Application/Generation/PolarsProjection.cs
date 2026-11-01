using System.Data;
using Packata.Core;

namespace Packata.Application.Generation;

internal static class PolarsProjection
{
    // These are target syntax projections of shared DbType interpretation, not source-type registrations.
    private static readonly IReadOnlyDictionary<DbType, string> Scalars = new Dictionary<DbType, string>
    {
        [DbType.SByte] = "Int8", [DbType.Byte] = "UInt8", [DbType.Int16] = "Int16", [DbType.UInt16] = "UInt16",
        [DbType.Int32] = "Int32", [DbType.UInt32] = "UInt32", [DbType.Int64] = "Int64", [DbType.UInt64] = "UInt64",
        [DbType.Single] = "Float32", [DbType.Double] = "Float64", [DbType.String] = "String",
        [DbType.Boolean] = "Boolean", [DbType.Date] = "Date", [DbType.Time] = "Time"
    };

    internal static string? Expression(FieldTypeResolution field, Dictionary<string, object?>[] children,
        Dictionary<string, object?>? items, List<GenerationDiagnostic> diagnostics)
    {
        if (field.LogicalType == "array" && items is not null)
            return ChildExpression(items) is { } item ? $"DataType.List({item})" : null;
        if (field.LogicalType == "object" && children.Length > 0)
        {
            if (children.Any(c => ChildExpression(c) is null)) return null;
            return "DataType.Struct(new Dictionary<string, DataType> { " + string.Join(", ", children.Select(c =>
                "[" + GenerationData.Literal((string)c["name"]!) + "] = " + ChildExpression(c))) + " })";
        }
        return ScalarExpression(field, diagnostics);
    }

    private static string? ScalarExpression(FieldTypeResolution field, List<GenerationDiagnostic> diagnostics)
    {
        if (field.DatabaseType is not { } database) return null;
        if (database == DbType.Decimal) return Decimal(field, diagnostics);
        if (database is DbType.DateTime or DbType.DateTimeOffset) return Datetime(field, diagnostics);
        if (database == DbType.Time && field.Parameters.TemporalPrecision > 9) return null;
        return Scalars.TryGetValue(database, out var scalar) ? "DataType." + scalar : null;
    }

    private static string? ChildExpression(Dictionary<string, object?> field)
        => ((Dictionary<string, object?>)field["target"]!)["type_expression"] as string;

    private static string? Decimal(FieldTypeResolution field, List<GenerationDiagnostic> diagnostics)
    {
        if (field.Parameters.Precision is not { } precision || field.Parameters.Scale is not { } scale)
        {
            diagnostics.Add(new("GEN_POLARS_DECIMAL", "Error", field.FieldPath,
                "Polars fixed decimal requires declared precision and scale; its defaults cannot describe every CLR decimal."));
            return null;
        }
        if (precision > 38) return null;
        return $"DataType.Decimal({precision}, {scale})";
    }

    private static string? Datetime(FieldTypeResolution field, List<GenerationDiagnostic> diagnostics)
    {
        var precision = field.Parameters.TemporalPrecision;
        if (precision > 9) return null;
        var unit = precision switch { <= 3 => "Milliseconds", <= 6 => "Microseconds", _ => "Nanoseconds" };
        var zone = field.Parameters.Timezone;
        if (field.Parameters.TemporalSemantics == TemporalSemantics.Offset && zone is null)
        {
            diagnostics.Add(new("GEN_POLARS_OFFSET", "Error", field.FieldPath,
                "A fixed-zone Polars datetime cannot preserve arbitrary per-row offsets. Declare a timezone or use a custom target."));
            return null;
        }
        if (field.Parameters.TemporalSemantics == TemporalSemantics.Instant) zone ??= "UTC";
        if (unit == "Nanoseconds")
        {
            diagnostics.Add(new("GEN_POLARS_RANGE", "Warning", field.FieldPath,
            "Nanosecond datetime storage has a narrower calendar range than CLR datetime; generated schema does not validate source bounds."));
        }

        if (zone is not null)
        {
            diagnostics.Add(new("GEN_POLARS_ZONE", "Warning", field.FieldPath,
            "Polars stores one timezone per column. Original per-value offsets are not retained."));
        }

        return $"DataType.Datetime(TimeUnit.{unit}, {(zone is null ? "null" : GenerationData.Literal(zone))})";
    }
}
