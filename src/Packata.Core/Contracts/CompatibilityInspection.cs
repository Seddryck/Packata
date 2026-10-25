namespace Packata.Core.Contracts;

// Validate mapping coverage and canonical inputs before applying compatibility rules.
internal sealed class CompatibilityInspection(List<CompatibilityDiagnostic> diagnostics,
    List<CompatibilityFinding> findings, CancellationToken cancellationToken)
{
    internal void Inspect(CompatibilityInput input, string name)
    {
        if (input.Contract is null)
            diagnostics.Add(new(name, "CMP003", MappingSeverity.Error, "$", "No canonical contract was supplied."));
        InspectDiagnostics(input.Diagnostics ?? [], name);
        InspectSource(input, name);
        if (input.Contract is { } contract) InspectAssets(contract, name);
    }

    private void InspectDiagnostics(IReadOnlyList<MappingDiagnostic> mappingDiagnostics, string name)
    {
        foreach (var diagnostic in mappingDiagnostics)
        {
            diagnostics.Add(new(name, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            if (diagnostic.Severity == MappingSeverity.Warning)
                Unknown(name, diagnostic.SourcePath, "mapping-warning", "Mapping reported ambiguity or unsupported semantics; compatibility is not established.");
        }
    }

    private void InspectSource(CompatibilityInput input, string name)
    {
        if (input.Standard != ContractStandard.Canonical && input.Descriptor is null)
            Unknown(name, "$", "source-coverage", "The original descriptor was not supplied; source mapping coverage cannot be established.");
        if (input.Descriptor is not { } descriptor) return;
        foreach (var path in CompatibilityCoverage.Unsupported(descriptor, input.Standard))
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(new(name, "CMP020", MappingSeverity.Warning, path, "Source semantics are not covered by this compatibility policy."));
            Unknown(name, path, "source-coverage", "Unsupported or unmapped source semantics prevent a complete compatibility verdict.");
        }
    }

    private void InspectAssets(DataContract contract, string name)
    {
        foreach (var asset in contract.Assets)
        {
            var path = ContractPath.Root("schema").Append(asset.Id);
            if (asset.Schema is null)
                Unknown(name, path.Value, "schema-coverage", "No schema is available for this dataset.");
            foreach (var field in asset.Schema?.Fields ?? [])
                CheckField(field, path.Append("properties").Append(field.Name), name);
        }
    }

    private void CheckField(DataField field, ContractPath path, string name)
    {
        if (field.Constraints.Any(x => string.IsNullOrEmpty(x.Kind))
            || field.Constraints.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count() != field.Constraints.Count)
            diagnostics.Add(new(name, "CMP004", MappingSeverity.Error, path.Value, "Constraint kinds must be non-empty and unique."));
        if (!CompatibilityRules.KnownType(field.LogicalType))
            Unknown(name, path.Append("type").Value, "type-coverage", "The field type is unspecified or unsupported by this policy.");
        foreach (var constraint in field.Constraints.Where(x => !CompatibilityRules.SupportedConstraintValue(x)))
            Unknown(name, path.Append("constraints").Append(constraint.Kind).Value, "constraint-coverage", "This constraint has no supported compatibility rule.");
        foreach (var child in field.Children) CheckField(child, path.Append("properties").Append(child.Name), name);
        if (field.Items is { } items) CheckField(items, path.Append("items"), name);
    }

    private void Unknown(string name, string path, string rule, string explanation)
        => findings.Add(new(new(path), CompatibilityClassification.Unknown, name + ":" + rule, explanation, null));
}
