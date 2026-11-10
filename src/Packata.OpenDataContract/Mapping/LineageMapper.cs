using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static partial class LineageMapper
{
    internal static FieldLineage[] Map(SchemaObject schema, IReadOnlyList<SchemaObject> schemas,
        ICollection<MappingDiagnostic> diagnostics)
        => schema.Properties.SelectMany(property => Map(property, [], schemas, diagnostics, $"schema/{schema.Name}/properties")).ToArray();

    private static IEnumerable<FieldLineage> Map(SchemaBaseProperty property, string[] parent,
        IReadOnlyList<SchemaObject> schemas, ICollection<MappingDiagnostic> diagnostics, string diagnosticPath, string? segment = null)
    {
        var fieldName = segment ?? property.PhysicalName ?? property.Name;
        string[] path = [..parent, fieldName];
        diagnosticPath = $"{diagnosticPath}/{property.Name}";
        if (property.TransformSourceObjects is not null || property.TransformLogic is not null || property.TransformDescription is not null)
            yield return MapTransformation(property, path, schemas, diagnostics, diagnosticPath);

        foreach (var child in property.Properties)
        {
            foreach (var lineage in Map(child, path, schemas, diagnostics, diagnosticPath + "/properties"))
                yield return lineage;
        }

        if (property.Items is not null)
        {
            foreach (var lineage in Map(property.Items, path, schemas, diagnostics, diagnosticPath + "/items", "[]"))
                yield return lineage;
        }

        if (property.Map is not null)
        {
            foreach (var lineage in Map(property.Map.Key, path, schemas, diagnostics, diagnosticPath + "/map/key", "key"))
                yield return lineage;
            foreach (var lineage in Map(property.Map.Value, path, schemas, diagnostics, diagnosticPath + "/map/value", "value"))
                yield return lineage;
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

        foreach (Match identifier in Identifier().Matches(logic))
        {
            var candidates = matches.SelectMany((match, index) => match[0].Properties
                .Where(property => property.Name == identifier.Value || property.PhysicalName == identifier.Value)
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
