using System.Collections;
using System.Data;
using System.Globalization;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders.Tabular;

internal static class InlineDataReader
{
    public static DataTable CreateTable(object? value, DataSchema? schema)
    {
        if (value is not IEnumerable sequence || value is string)
            throw new ArgumentException("The value must be an enumerable collection of rows.", nameof(value));

        var rows = sequence.Cast<object?>().ToArray();
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        var fields = schema?.Fields ?? [];
        if (fields.Count > 0)
        {
            var mapper = new RuntimeTypeMapper();
            foreach (var field in fields)
                table.Columns.Add(field.Name, mapper.Map(field.LogicalType, field.Format));
        }
        else if (rows.FirstOrDefault() is object first)
        {
            foreach (var name in ReadMap(first).Keys)
                table.Columns.Add(name, typeof(object));
        }

        foreach (var row in rows)
            table.Rows.Add(ReadValues(row, table, fields.Count > 0));
        return table;
    }

    private static object[] ReadValues(object? row, DataTable table, bool hasSchema)
    {
        if (TryReadMap(row, out var map))
            return table.Columns.Cast<DataColumn>()
                .Select(column => map.TryGetValue(column.ColumnName, out var value)
                    ? ConvertValue(value, column.DataType)
                    : DBNull.Value)
                .ToArray();

        if (!hasSchema)
            throw new ArgumentException("Positional inline rows require a schema.", nameof(row));
        if (row is not IEnumerable sequence || row is string)
            throw new ArgumentException(
                "Each inline row must be a string-keyed dictionary or positional collection.", nameof(row));

        var values = sequence.Cast<object?>().ToArray();
        if (values.Length != table.Columns.Count)
            throw new ArgumentException(
                $"A positional inline row contains {values.Length} values but the schema defines {table.Columns.Count} fields.",
                nameof(row));
        return values.Select((item, index) => ConvertValue(item, table.Columns[index].DataType)).ToArray();
    }

    private static IReadOnlyDictionary<string, object?> ReadMap(object row)
        => TryReadMap(row, out var map)
            ? map
            : throw new ArgumentException(
                "Positional inline rows require a schema; rows without a schema must be string-keyed dictionaries.",
                nameof(row));

    private static bool TryReadMap(object? row, out IReadOnlyDictionary<string, object?> map)
    {
        if (row is IReadOnlyDictionary<string, object?> readOnly)
        {
            map = readOnly;
            return true;
        }

        if (row is IDictionary dictionary)
        {
            var values = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                    throw new ArgumentException("Inline row dictionary keys must be strings.", nameof(row));
                values[key] = entry.Value;
            }
            map = values;
            return true;
        }

        map = null!;
        return false;
    }

    private static object ConvertValue(object? value, Type targetType)
    {
        if (value is null or DBNull) return DBNull.Value;
        if (targetType == typeof(object) || targetType.IsInstanceOfType(value)) return value;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(DateOnly) && DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date))
            return date;
        if (targetType == typeof(TimeOnly) && TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var time))
            return time;
        if (targetType == typeof(Guid) && Guid.TryParse(text, out var guid)) return guid;
        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
