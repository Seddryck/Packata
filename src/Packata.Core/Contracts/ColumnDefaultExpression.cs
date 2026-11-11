using System.Collections;

namespace Packata.Core.Contracts;

/// <summary>A database-evaluated expression declared for a column default.</summary>
public sealed record ColumnDefaultExpression(ColumnDefaultKind Kind, int? FractionalSecondsPrecision = null,
    ColumnDefaultSequence? Sequence = null);

public enum ColumnDefaultKind
{
    CurrentTimestamp,
    CurrentUtcTimestamp,
    CurrentDate,
    CurrentTime,
    NextSequenceValue
}

/// <summary>The literal identifier components of an externally managed sequence.</summary>
public sealed record ColumnDefaultSequence(string Name, string? Schema = null, string? Database = null, string? Catalog = null);

internal static partial class ColumnDefaultDeclarations
{
    internal static DataField Apply(DataField field, IEnumerable<(string? Name, object? Value)> properties,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var defaults = properties.Where(property => property.Name is "defaultValue" or "defaultExpression").ToArray();
        if (defaults.Length == 0) return field;
        if (defaults.Length > 1)
        {
            diagnostics.Add(new("DEFAULT001", MappingSeverity.Error, path,
                $"Field '{path}' must declare only one defaultValue or defaultExpression."));
            return field;
        }

        return defaults[0].Name == "defaultValue"
            ? field with { DefaultValue = ReadLiteral(defaults[0].Value, path, diagnostics) }
            : field with { DefaultExpression = ReadExpression(defaults[0].Value, path, diagnostics) };
    }

    private static ColumnDefaultExpression? ReadExpression(object? value, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        try
        {
            return ParseExpression(value);
        }
        catch (ArgumentException error)
        {
            diagnostics.Add(new("DEFAULT002", MappingSeverity.Error, path,
                $"Invalid defaultExpression for field '{path}'. {error.Message}"));
            return null;
        }
    }

    private static ColumnDefaultExpression ParseExpression(object? value)
    {
        if (value is not IDictionary expression || expression["kind"] is not string kind)
            throw new ArgumentException("Supply a defaultExpression object with a string 'kind'.");

        return kind switch
        {
            "current-timestamp" => new(ColumnDefaultKind.CurrentTimestamp, Precision(expression)),
            "current-utc-timestamp" => new(ColumnDefaultKind.CurrentUtcTimestamp, Precision(expression)),
            "current-time" => new(ColumnDefaultKind.CurrentTime, Precision(expression)),
            "current-date" => RequireKeys(expression, "kind") ? new(ColumnDefaultKind.CurrentDate)
                : throw new ArgumentException("current-date accepts only 'kind'."),
            "next-sequence-value" => new(ColumnDefaultKind.NextSequenceValue, Sequence: ReadSequence(expression)),
            _ => throw new ArgumentException($"Unsupported defaultExpression kind '{kind}'.")
        };
    }

    private static int? Precision(IDictionary expression)
    {
        if (!RequireKeys(expression, "kind", "fractionalSecondsPrecision"))
            throw new ArgumentException("A temporal default accepts only 'kind' and 'fractionalSecondsPrecision'.");
        if (!expression.Contains("fractionalSecondsPrecision")) return null;
        var precision = expression["fractionalSecondsPrecision"];
        if (precision is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            var number = Convert.ToDecimal(precision, System.Globalization.CultureInfo.InvariantCulture);
            if (number is >= 0 and <= 9) return (int)number;
        }

        throw new ArgumentException("fractionalSecondsPrecision must be an integer from 0 through 9.");
    }

    private static ColumnDefaultSequence ReadSequence(IDictionary expression)
    {
        if (!RequireKeys(expression, "kind", "sequence") || expression["sequence"] is not IDictionary sequence
            || !RequireKeys(sequence, "name", "schema", "database", "catalog"))
            throw new ArgumentException("A sequence default requires a 'sequence' object with 'name' and supported qualifiers.");

        var name = Identifier(sequence, "name");
        var schema = Identifier(sequence, "schema");
        var database = Identifier(sequence, "database");
        var catalog = Identifier(sequence, "catalog");
        if (name is null || (schema is null && database is null && catalog is null))
            throw new ArgumentException("A sequence requires a 'name' and at least one of 'schema', 'database', or 'catalog'.");
        return new(name, schema, database, catalog);
    }

    private static string? Identifier(IDictionary sequence, string key)
    {
        if (!sequence.Contains(key)) return null;
        if (sequence[key] is string value && !string.IsNullOrWhiteSpace(value) && !value.Contains('\0')) return value;
        throw new ArgumentException($"Sequence '{key}' must be a non-empty identifier without NUL characters.");
    }

    private static bool RequireKeys(IDictionary map, params string[] names)
        => map.Keys.Cast<object>().All(key => key is string name && names.Contains(name, StringComparer.Ordinal));
}
