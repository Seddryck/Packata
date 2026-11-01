using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

/// <summary>Packata schema-object extension semantics; original declarations remain in ODCS metadata.</summary>
internal static class PropertyRequirementsMapper
{
    internal static (bool AllowAdditional, bool EnforceOrder) Map(SchemaObject schema,
        ICollection<MappingDiagnostic> diagnostics)
    {
        var path = $"schema/{schema.Name}/customProperties";
        foreach (var (property, index) in schema.CustomProperties.Select((p, i) => (p, i)))
            if (property is { Vendor: "packata", Property: "propertyMatching" or "propertyOrder" })
            {
                diagnostics.Add(new("ODCS008", MappingSeverity.Error, $"{path}/{index}",
                    "Obsolete Packata propertyMatching/propertyOrder: use allowAdditionalProperties and enforcePropertyOrder booleans, and per-property required."));
            }

        return (Read(schema.CustomProperties, "allowAdditionalProperties", path, false, diagnostics),
            Read(schema.CustomProperties, "enforcePropertyOrder", path, true, diagnostics));
    }

    private static bool Read(CustomProperties properties, string name, string path,
        bool fallback, ICollection<MappingDiagnostic> diagnostics)
    {
        var matches = properties.Select((property, index) => (property, index))
            .Where(x => x.property.Vendor == "packata" && x.property.Property == name).ToArray();
        if (matches.Length == 0) return fallback;
        if (matches.Length == 1 && matches[0].property.Value is bool value) return value;
        foreach (var match in matches)
        {
            diagnostics.Add(new("ODCS008", MappingSeverity.Error, $"{path}/{match.index}",
                $"Packata {name} must be declared once with a boolean value: false or true."));
        }

        return fallback;
    }

    internal static void CheckPlacement(CustomProperties? properties, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (properties is null) return;
        for (var index = 0; index < properties.Count; index++)
        {
            if (properties[index] is { Vendor: "packata", Property: "propertyMatching" or "propertyOrder" or "allowAdditionalProperties" or "enforcePropertyOrder" })
            {
                diagnostics.Add(new("ODCS008", MappingSeverity.Error, $"{path}/customProperties/{index}",
                    "Packata allowAdditionalProperties and enforcePropertyOrder are supported only on schema objects; propertyMatching/propertyOrder are obsolete."));
            }
        }
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
