using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

/// <summary>Packata schema-object extension semantics; original declarations remain in ODCS metadata.</summary>
internal static class PropertyMatchingMapper
{
    internal static (PropertyMatching Matching, PropertyOrder Order) Map(SchemaObject schema,
        ICollection<MappingDiagnostic> diagnostics)
    {
        var path = $"schema/{schema.Name}/customProperties";
        var matching = Read(schema.CustomProperties, "propertyMatching", path,
            new Dictionary<string, PropertyMatching>(StringComparer.Ordinal)
            {
                ["equal"] = PropertyMatching.Equal, ["subset"] = PropertyMatching.Subset,
                ["superset"] = PropertyMatching.Superset, ["partial"] = PropertyMatching.Partial
            }, PropertyMatching.Equal, diagnostics);
        var order = Read(schema.CustomProperties, "propertyOrder", path,
            new Dictionary<string, PropertyOrder>(StringComparer.Ordinal)
            { ["any"] = PropertyOrder.Any, ["declared"] = PropertyOrder.Declared }, PropertyOrder.Declared, diagnostics);
        return (matching, order);
    }

    private static T Read<T>(CustomProperties properties, string name, string path,
        IReadOnlyDictionary<string, T> values, T fallback, ICollection<MappingDiagnostic> diagnostics)
    {
        var matches = properties.Select((property, index) => (property, index))
            .Where(x => x.property.Vendor == "packata" && x.property.Property == name).ToArray();
        if (matches.Length == 0) return fallback;
        if (matches.Length == 1 && matches[0].property.Value is string text && values.TryGetValue(text, out var value)) return value;
        foreach (var match in matches)
            diagnostics.Add(new("ODCS008", MappingSeverity.Error, $"{path}/{match.index}",
                $"Packata {name} must be declared once with a string value: {string.Join(", ", values.Keys)}."));
        return fallback;
    }

    internal static void CheckPlacement(CustomProperties? properties, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (properties is null) return;
        for (var index = 0; index < properties.Count; index++)
            if (properties[index] is { Vendor: "packata", Property: "propertyMatching" or "propertyOrder" })
                diagnostics.Add(new("ODCS008", MappingSeverity.Error, $"{path}/customProperties/{index}",
                    "Packata propertyMatching and propertyOrder are supported only on schema objects."));
    }

    internal static void CheckPlacements(DataContract document, ICollection<MappingDiagnostic> diagnostics)
        => new PlacementVisitor(diagnostics).Visit(document, "$");

    private sealed class PlacementVisitor(ICollection<MappingDiagnostic> diagnostics)
    {
        private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);

        internal void Visit(object? value, string path)
        {
            if (value is null or string || !_seen.Add(value)) return;
            if (value is System.Collections.IEnumerable list)
            {
                VisitSequence(list, path);
                return;
            }
            if (value.GetType().Namespace?.StartsWith("Packata.OpenDataContract", StringComparison.Ordinal) != true) return;
            VisitMembers(value, path);
        }

        private void VisitSequence(System.Collections.IEnumerable list, string path)
        {
            var index = 0;
            foreach (var item in list) Visit(item, $"{path}/{index++}");
        }

        private void VisitMembers(object value, string path)
        {
            foreach (var member in value.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            {
                if (member.GetValue(value) is not { } child) continue;
                VisitMember(value, child, member.Name, path);
            }
        }

        private void VisitMember(object owner, object child, string memberName, string path)
        {
            if (child is CustomProperties properties)
            {
                if (owner is not SchemaObject) CheckPlacement(properties, path, diagnostics);
                return;
            }
            Visit(child, $"{path}/{char.ToLowerInvariant(memberName[0])}{memberName[1..]}");
        }
    }
}
