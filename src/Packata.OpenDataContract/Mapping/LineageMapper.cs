using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static partial class LineageMapper
{
    internal static FieldLineage[] Map(SchemaObject schema, IReadOnlyList<SchemaObject> schemas,
        ICollection<MappingDiagnostic> diagnostics)
        => schema.Properties.SelectMany(property => Map(property, [property.PhysicalName ?? property.Name], schemas, diagnostics,
            $"schema/{schema.Name}/properties/{property.Name}")).ToArray();

    private static IEnumerable<FieldLineage> Map(SchemaBaseProperty property, string[] path,
        IReadOnlyList<SchemaObject> schemas, ICollection<MappingDiagnostic> diagnostics, string diagnosticPath)
    {
        if (property.TransformSourceObjects is not null || property.TransformLogic is not null || property.TransformDescription is not null)
            yield return MapTransformation(property, path, schemas, diagnostics, diagnosticPath);

        foreach (var (child, segment, diagnosticSegment) in Children(property))
        {
            foreach (var lineage in Map(child, [..path, segment], schemas, diagnostics, $"{diagnosticPath}/{diagnosticSegment}"))
                yield return lineage;
        }
    }

    private static IEnumerable<(SchemaBaseProperty Property, string Segment, string DiagnosticSegment)> Children(SchemaBaseProperty property)
    {
        foreach (var child in property.Properties)
            yield return (child, child.PhysicalName ?? child.Name, $"properties/{child.Name}");

        if (property.Items is { } items)
            yield return (items, "[]", $"items/{items.Name}");

        if (property.Map is { } map)
        {
            yield return (map.Key, "key", $"map/key/{map.Key.Name}");
            yield return (map.Value, "value", $"map/value/{map.Value.Name}");
        }
    }

    private static FieldLineage MapTransformation(SchemaBaseProperty property, string[] path,
        IReadOnlyList<SchemaObject> schemas, ICollection<MappingDiagnostic> diagnostics, string diagnosticPath)
    {
        var references = (property.TransformSourceObjects ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var matches = references.Select(reference =>
            schemas.Where(schema => schema.Name == reference || schema.PhysicalName == reference).ToArray()).ToArray();
        var fields = references.Select(_ => new List<string>()).ToArray();
        var resolved = TryResolve(property.TransformLogic, matches, fields, out var identity);
        if (!resolved)
        {
            diagnostics.Add(new("ODCS009", MappingSeverity.Warning, diagnosticPath,
                "Transformation columns could not be resolved unambiguously. The declared sources and transformation metadata were retained."));
        }

        var sources = references.Select((reference, index) => new LineageSource(
            reference,
            matches[index].Length == 1 ? matches[index][0].Name : null,
            resolved ? fields[index].Distinct(StringComparer.Ordinal).ToArray() : [])).ToArray();
        return new(path, sources, property.TransformLogic, property.TransformDescription, resolved && identity);
    }

    private static bool TryResolve(string? logic, SchemaObject[][] matches, List<string>[] fields, out bool identity)
    {
        identity = false;
        if (logic is null || matches.Length == 0 || matches.Any(match => match.Length != 1) || !Arithmetic().IsMatch(logic))
            return false;

        foreach (var identifier in Identifier().Matches(logic).Select(match => match.Value))
        {
            var candidates = matches.SelectMany((match, index) => match[0].Properties
                .Where(property => property.Name == identifier || property.PhysicalName == identifier)
                .Select(property => (Index: index, Field: property.PhysicalName ?? property.Name))).ToArray();
            if (candidates.Length != 1) return false;
            fields[candidates[0].Index].Add(candidates[0].Field);
        }

        identity = Identifier().Match(logic).Value == logic.Trim();
        return true;
    }

    private const string Operand = @"(?:[a-zA-Z_][a-zA-Z_0-9]*|[0-9]+(?:\.[0-9]+)?)";

    // ODCS permits arbitrary text. Only infer column edges for this restricted expression grammar.
    [GeneratedRegex(@"\A\s*" + Operand + @"(?:\s*[-+*/%]\s*" + Operand + @")*\s*\z", RegexOptions.NonBacktracking)]
    private static partial Regex Arithmetic();

    [GeneratedRegex(@"[a-zA-Z_][a-zA-Z_0-9]*", RegexOptions.NonBacktracking)]
    private static partial Regex Identifier();
}
