using Apache.Arrow;
using Apache.Arrow.Types;
using Packata.Core.Contracts;

namespace Packata.Arrow;

public static class ArrowSchemaMapper
{
    public static Schema Map(DataSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Fields.Count == 0) throw new NotSupportedException("Arrow conversion requires at least one field.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        return new Schema(schema.Fields.Select(field =>
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name))
                throw Unsupported(field, "Field names must be nonempty and unique.");
            return new Field(field.Name, MapType(field), field.Nullable);
        }), null);
    }

    private static IArrowType MapType(DataField field)
    {
        if (field.Children.Count != 0 || field.Items is not null)
            throw Unsupported(field, "Nested fields are unsupported.");
        return field.LogicalType?.ToLowerInvariant() switch
        {
            "boolean" => BooleanType.Default,
            "integer" => Integer(field),
            "number" when Parameter(field, "precision") is null && Parameter(field, "scale") is null => DoubleType.Default,
            "number" or "decimal" => Decimal(field),
            "string" => StringType.Default,
            "binary" => BinaryType.Default,
            "date" => Date32Type.Default,
            "timestamp" or "datetime" => Timestamp(field),
            _ => throw Unsupported(field, $"Unsupported logical type '{field.LogicalType}'.")
        };
    }

    private static IArrowType Integer(DataField field)
    {
        var width = Parameter(field, "integerWidth") ?? 64;
        var signed = Parameter(field, "signed") ?? true;
        return (width, signed) switch
        {
            (8, true) => Int8Type.Default, (16, true) => Int16Type.Default,
            (32, true) => Int32Type.Default, (64, true) => Int64Type.Default,
            (8, false) => UInt8Type.Default, (16, false) => UInt16Type.Default,
            (32, false) => UInt32Type.Default, (64, false) => UInt64Type.Default,
            _ => throw Unsupported(field, "integerWidth must be an Int32 (8/16/32/64) and signed a Boolean.")
        };
    }

    private static Decimal128Type Decimal(DataField field)
    {
        if (Parameter(field, "precision") is not int precision || Parameter(field, "scale") is not int scale
            || precision is < 1 or > 38 || scale < 0 || scale > precision)
            throw Unsupported(field, "Decimal requires Int32 precision (1..38) and scale (0..precision).");
        return new Decimal128Type(precision, scale);
    }

    private static TimestampType Timestamp(DataField field)
    {
        // Explicit unit names; default to microseconds. No local timezone inference.
        var unit = Parameter(field, "precision") switch
        {
            null or "microsecond" => TimeUnit.Microsecond,
            "second" => TimeUnit.Second,
            "millisecond" => TimeUnit.Millisecond,
            "nanosecond" => TimeUnit.Nanosecond,
            _ => throw Unsupported(field, "Timestamp precision must be second, millisecond, microsecond or nanosecond.")
        };
        if (Parameter(field, "defaultTimezone") is not null)
            throw Unsupported(field, "defaultTimezone requires interpretation outside the adapter.");
        var timezone = Parameter(field, "timezone") switch
        {
            null or true => "UTC",
            false => "",
            _ => throw Unsupported(field, "timezone must be a Boolean.")
        };
        return new TimestampType(unit, timezone);
    }

    internal static object? Parameter(DataField field, string name)
    {
        var values = field.Constraints.Where(c => c.Kind == name).ToArray();
        if (values.Length > 1) throw Unsupported(field, $"Duplicate '{name}' parameter.");
        return values.SingleOrDefault()?.Value;
    }

    internal static NotSupportedException Unsupported(DataField field, string message) => new($"Field '{field.Name}': {message}");
}
