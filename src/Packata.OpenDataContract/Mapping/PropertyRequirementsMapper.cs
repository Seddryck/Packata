using Packata.AdapterMapping;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

/// <summary>Packata schema-object extension semantics; original declarations remain in ODCS metadata.</summary>
internal static class PropertyRequirementsMapper
{
    internal static (bool AllowAdditional, bool EnforceOrder) Map(SchemaObject schema,
        ICollection<MappingDiagnostic> diagnostics, OdcsBindings bindings)
    {
        var path = $"schema/{schema.Name}/customProperties";
        return (Read(schema.CustomProperties, bindings.AllowAdditionalProperties, path, false, diagnostics),
            Read(schema.CustomProperties, bindings.EnforcePropertyOrder, path, true, diagnostics));
    }

    private static bool Read(CustomProperties properties, BindingSelector selector, string path,
        bool fallback, ICollection<MappingDiagnostic> diagnostics)
    {
        var matches = properties.Select((property, index) => (property, index))
            .Where(x => selector.Matches(x.property.Property, x.property.Vendor)).ToArray();
        if (matches.Length == 0) return fallback;
        if (matches.Length == 1 && OdcsBindings.ReadValue(selector, matches[0].property) is bool value) return value;
        foreach (var match in matches)
        {
            diagnostics.Add(new("ODCS008", MappingSeverity.Error, $"{path}/{match.index}",
                $"Custom property '{match.property.Property}' for '{selector.Id}' must be declared once with a boolean value: false or true."));
        }

        return fallback;
    }

    internal static void CheckPlacements(DataContract document, ICollection<MappingDiagnostic> diagnostics, OdcsBindings bindings)
        => new PlacementVisitor(diagnostics, bindings).Visit(document, "$");

    private sealed class PlacementVisitor(ICollection<MappingDiagnostic> diagnostics, OdcsBindings bindings)
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
                var childPath = $"{path}/{char.ToLowerInvariant(member.Name[0])}{member.Name[1..]}";
                if (child is CustomProperties properties) CheckPlacement(value, properties, childPath);
                else Visit(child, childPath);
            }
        }

        private void CheckPlacement(object owner, CustomProperties properties, string path)
        {
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties[index];
                var sourcePath = $"{path}/{index}";
                if (property is { Vendor: "packata", Property: "propertyMatching" or "propertyOrder" })
                {
                    if (owner is SchemaObject schema) sourcePath = $"schema/{schema.Name}/customProperties/{index}";
                    diagnostics.Add(new("ODCS008", MappingSeverity.Error, sourcePath,
                        $"Obsolete Packata property '{property.Property}'. Use schema.allowAdditionalProperties, schema.enforcePropertyOrder and per-property required."));
                }
                else if (bindings.Misplaced(owner, property) is { } selector)
                {
                    diagnostics.Add(new("ODCS008", MappingSeverity.Error, sourcePath,
                        $"Custom property '{property.Property}' for '{selector.Id}' is declared on the wrong source owner."));
                }
            }
        }
    }
}
