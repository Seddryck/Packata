namespace Packata.Core.Contracts;

internal sealed class SatisfactionAnalysis(SatisfactionInput producer, SatisfactionInput consumer,
    SatisfactionPolicy policy, CancellationToken cancellationToken)
{
    private readonly List<SatisfactionFinding> findings = [];
    private readonly List<SatisfactionDiagnostic> diagnostics = [];

    internal ContractSatisfaction Run()
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (producer.Standard != consumer.Standard || !Enum.IsDefined(producer.Standard))
                Error("analysis", null, "SAT001", "Only same-standard producer/consumer pairs are supported.");
            Inspect(producer, "producer");
            Inspect(consumer, "consumer");
            if (diagnostics.Any(x => x.Severity == MappingSeverity.Error)) return Result(false);
            CompareAssets();
            cancellationToken.ThrowIfCancellationRequested();
            return Result(true);
        }
        catch (OperationCanceledException)
        {
            Error("analysis", null, "SAT130", "Satisfaction analysis was cancelled.");
            return Result(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Error("analysis", null, "SAT002", "Invalid canonical identifiers or values prevented satisfaction analysis.");
            return Result(false);
        }
    }

    private ContractSatisfaction Result(bool completed) => new(producer, consumer, policy, completed, findings, diagnostics);
    private void Error(string role, string? source, string code, string message)
        => diagnostics.Add(new(role, source, code, MappingSeverity.Error, "$", message));

    private void Inspect(SatisfactionInput input, string role)
    {
        if (input.Contract is null) { Error(role, input.Source, "SAT003", "No canonical document was supplied."); return; }
        foreach (var diagnostic in input.Diagnostics ?? [])
        {
            diagnostics.Add(new(role, input.Source, diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, diagnostic.Message));
            if (diagnostic.Severity == MappingSeverity.Warning)
                SourceFinding(role, diagnostic.SourcePath, "SAT020", SatisfactionStatus.Unknown, "Mapping diagnostics leave source semantics unresolved.");
        }
        InspectSource(input, role);
        InspectIdentifiers(input.Contract, role, input.Source);
        // Reuse the canonical differ's identifier/duplicate validation without comparing versions.
        _ = input.Contract.Diff(input.Contract);
        InspectModel(input.Contract, role);
    }

    private void InspectIdentifiers(DataContract contract, string role, string? source)
    {
        Check(contract.Assets.Select(x => x.Id), "schema");
        foreach (var asset in contract.Assets)
            Fields(asset.Schema?.Fields ?? [], ContractPath.Root("schema").Append(asset.Id).Append("properties"));

        void Check(IEnumerable<string> names, string path)
        {
            var values = names.ToArray();
            if (!values.Any(string.IsNullOrWhiteSpace) && values.Distinct(StringComparer.Ordinal).Count() == values.Length) return;
            diagnostics.Add(new(role, source, "SAT004", MappingSeverity.Error, path, "Missing or ambiguous semantic identifiers prevent deterministic matching."));
            SourceFinding(role, path, "SAT004", SatisfactionStatus.Unknown, "Matching is unresolved because semantic identifiers are missing or ambiguous.");
        }
        void Fields(IReadOnlyList<DataField> fields, ContractPath path)
        {
            Check(fields.Select(x => x.Name), path.Value);
            foreach (var field in fields)
            {
                Fields(field.Children, path.Append(field.Name).Append("properties"));
                if (field.Items is { } items) Fields(items.Children, path.Append(field.Name).Append("items").Append("properties"));
            }
        }
    }

    private void InspectSource(SatisfactionInput input, string role)
    {
        if (input.Standard == ContractStandard.Canonical) return;
        if (input.Descriptor is null)
        {
            SourceFinding(role, "$", "SAT021", SatisfactionStatus.Unknown, "Original source coverage is unavailable.");
            return;
        }
        foreach (var path in CompatibilityCoverage.Unsupported(input.Descriptor, input.Standard, satisfactionFacets: true))
        {
            var informative = IsInformational(path);
            SourceFinding(role, path, informative ? "SAT090" : "SAT022",
                informative ? SatisfactionStatus.Informational : SatisfactionStatus.Unknown,
                informative ? "Descriptive or ownership metadata is informational." : "Native semantics have no supported satisfaction rule.");
        }
    }

    private static bool IsInformational(string path) => new[] { "$.team", "$.support", "$.roles", "$.owner", "$.contributors", "$.authoritativeDefinitions" }
        .Any(prefix => path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal) || path.StartsWith(prefix + "[", StringComparison.Ordinal));

    private void InspectModel(DataContract contract, string role)
    {
        SourceFinding(role, "$", "SAT090", SatisfactionStatus.Informational,
            "Identity, version, descriptive metadata, ownership, and endpoint locations are retained as information, not equality requirements.");
        if (contract.Sla is not null || contract.Lifecycle is not null || contract.Governance.ServiceLevels.Count > 0)
            SourceFinding(role, "sla/lifecycle", "SAT023", SatisfactionStatus.Unknown, "SLA and lifecycle declarations are not evaluated or executed.");
        if (SatisfactionExtensionCoverage.HasUnresolved(contract.Extensions))
            SourceFinding(role, "extensions", "SAT024", SatisfactionStatus.Unknown, "Contract extension semantics are unresolved.");
        foreach (var asset in contract.Assets)
            InspectAsset(asset, role);
    }

    private void InspectAsset(DataAsset asset, string role)
    {
        var path = ContractPath.Root("schema").Append(asset.Id);
        if (asset.Schema is null) SourceFinding(role, path.Value, "SAT025", SatisfactionStatus.Unknown, "No dataset schema is available.");
        if (HasUnsupportedAssetSemantics(asset))
            SourceFinding(role, path.Value, "SAT026", SatisfactionStatus.Unknown, "Quality, keys, relationships, category tables, or extension semantics are unresolved.");
        if (asset.Schema is { } schema && SatisfactionExtensionCoverage.HasUnresolved(schema.Extensions))
            SourceFinding(role, path.Value, "SAT026", SatisfactionStatus.Unknown, "Schema extension semantics are unresolved.");
        if (asset.Schema?.PropertyMatching == PropertyMatching.Partial)
            SourceFinding(role, path.Value, "SAT026", SatisfactionStatus.Unknown, "Partial field-set matching is unsupported.");
        foreach (var field in asset.Schema?.Fields ?? []) InspectField(field, role, path.Append("properties").Append(field.Name));
    }

    private static bool HasUnsupportedAssetSemantics(DataAsset asset) => asset.QualityRules.Count > 0
        || asset.Schema?.Relationships.Count > 0 || asset.Schema?.PrimaryKey.Count > 0
        || SatisfactionExtensionCoverage.HasUnresolved(asset.Extensions) || asset.CategoryTable is not null;

    private void InspectField(DataField field, string role, ContractPath path)
    {
        if (!CompatibilityRules.KnownType(field.LogicalType))
            SourceFinding(role, path.Append("type").Value, "SAT027", SatisfactionStatus.Unknown, "Custom or unspecified type semantics are unresolved.");
        InspectFieldMetadata(field, role, path);
        foreach (var constraint in field.Constraints)
            InspectConstraint(constraint, role, path);
        foreach (var child in field.Children) InspectField(child, role, path.Append("properties").Append(child.Name));
        if (field.Items is { } items) InspectField(items, role, path.Append("items"));
    }

    private void InspectFieldMetadata(DataField field, string role, ContractPath path)
    {
        if (field.QualityRules.Count > 0 || SatisfactionExtensionCoverage.HasUnresolved(field.Extensions) || field.PhysicalType is not null || field.Format is not null)
            SourceFinding(role, path.Value, "SAT028", SatisfactionStatus.Unknown, "Quality, physical capacity, format, or field extension semantics are unresolved.");
    }

    private void InspectConstraint(DataConstraint constraint, string role, ContractPath path)
    {
        if (!CompatibilityRules.SupportedConstraintValue(constraint) && constraint.Kind is not ("precision" or "scale"))
            SourceFinding(role, path.Append("constraints").Append(constraint.Kind).Value, "SAT029", SatisfactionStatus.Unknown, "Unsupported constraint semantics remain unresolved.");
    }

    private void SourceFinding(string role, string path, string code, SatisfactionStatus status, string message)
        => findings.Add(new(code, status, role == "producer" ? path : "$", role == "consumer" ? path : "$", null, null, role + ": " + message));

    private void CompareAssets()
    {
        var offered = producer.Contract!.Assets.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var accepted in consumer.Contract!.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            offered.TryGetValue(accepted.Id, out var asset);
            var path = ContractPath.Root("schema").Append(accepted.Id);
            Add("SAT100", asset is null ? SatisfactionStatus.Unsatisfied : SatisfactionStatus.Satisfied, path, asset?.Id, accepted.Id, "Dataset identity match.");
            CompareSchemaMatching(asset?.Schema, accepted.Schema, path);
            CompareFields(asset?.Schema?.Fields, accepted.Schema?.Fields ?? [], path.Append("properties"), asset is not null,
                accepted.Schema?.PropertyMatching is PropertyMatching.Equal or PropertyMatching.Superset);
        }
        foreach (var extra in offered.Keys.Except(consumer.Contract.Assets.Select(x => x.Id), StringComparer.Ordinal))
            Extra(ContractPath.Root("schema").Append(extra));
    }

    private void CompareFields(IReadOnlyList<DataField>? offered, IReadOnlyList<DataField> accepted, ContractPath path,
        bool parentExists, bool rejectExtras = false)
    {
        var index = (offered ?? []).ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var field in accepted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index.TryGetValue(field.Name, out var produced);
            var fieldPath = path.Append(field.Name);
            var status = Presence(produced, field, offered is not null && parentExists);
            Add("SAT101", status, fieldPath, produced?.Name, field.Name, "Field identity and native optionality.");
            if (produced is null && !field.Required && offered is not null && parentExists)
            {
                Add("SAT091", SatisfactionStatus.Informational, fieldPath, null, field.Name, "Absent optional field: value constraints are not applicable.");
                continue;
            }
            CompareField(produced, field, fieldPath);
        }
        foreach (var extra in index.Keys.Except(accepted.Select(x => x.Name), StringComparer.Ordinal)) Extra(path.Append(extra), rejectExtras);
    }

    private void CompareSchemaMatching(DataSchema? offered, DataSchema? accepted, ContractPath path)
    {
        if (accepted is null || offered is null) return;
        var producerNames = offered.Fields.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var consumerNames = accepted.Fields.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var matching = accepted.PropertyMatching;
        var partial = matching == PropertyMatching.Partial || offered.PropertyMatching == PropertyMatching.Partial;
        var guaranteesPresence = offered.PropertyMatching is PropertyMatching.Equal or PropertyMatching.Subset;
        var guaranteesClosed = offered.PropertyMatching is PropertyMatching.Equal or PropertyMatching.Superset;
        var fits = matching switch
        {
            PropertyMatching.Equal => guaranteesPresence && guaranteesClosed && producerNames.SetEquals(consumerNames),
            PropertyMatching.Subset => guaranteesPresence && consumerNames.IsSubsetOf(producerNames),
            PropertyMatching.Superset => guaranteesClosed && producerNames.IsSubsetOf(consumerNames),
            _ => false
        };
        Add("SAT103", partial ? SatisfactionStatus.Unknown : fits ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied,
            path.Append("propertyMatching"), offered.PropertyMatching, matching,
            partial ? "Partial field-set containment is unresolved." : "Producer property-set guarantees must fit consumer requirements.");
        var expected = accepted.Fields.Select(x => x.Name).Where(producerNames.Contains);
        var actual = offered.Fields.Select(x => x.Name).Where(consumerNames.Contains);
        var orderFits = accepted.PropertyOrder == PropertyOrder.Any
            || offered.PropertyOrder == PropertyOrder.Declared && expected.SequenceEqual(actual, StringComparer.Ordinal);
        Add("SAT104", orderFits ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied,
            path.Append("propertyOrder"), offered.PropertyOrder, accepted.PropertyOrder,
            "Producer ordering guarantees must fit the relative order of consumer-declared properties.");
    }
    private static SatisfactionStatus Presence(DataField? offered, DataField accepted, bool known)
        => !known ? SatisfactionStatus.Unknown : offered is not null || !accepted.Required ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied;

    private void CompareField(DataField? offered, DataField accepted, ContractPath path)
    {
        var type = offered is null ? SatisfactionStatus.Unknown : Convert(CompatibilityRules.CompareTypes(accepted.LogicalType, offered.LogicalType));
        Add("SAT110", type, path.Append("type"), offered?.LogicalType, accepted.LogicalType, "Offered logical values must fit the accepted type.");
        ComparePresenceFacets(offered, accepted, path);
        new SatisfactionFacetComparison(Add).Compare(offered, accepted, path, type);
        CompareFields(offered?.Children, accepted.Children, path.Append("properties"), offered is not null);
        if (accepted.Items is { } items) CompareField(offered?.Items, items, path.Append("items"));
    }

    private void ComparePresenceFacets(DataField? offered, DataField accepted, ContractPath path)
    {
        Add("SAT111", offered is null ? SatisfactionStatus.Unknown : !accepted.Required || offered.Required ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied,
            path.Append("required"), offered?.Required, accepted.Required, "Required field presence is independent of nullability.");
        var consumerNull = accepted.Nullable;
        var producerNull = offered?.Nullable;
        Add("SAT112", offered is null ? SatisfactionStatus.Unknown : consumerNull || producerNull == false ? SatisfactionStatus.Satisfied : SatisfactionStatus.Unsatisfied,
            path.Append("nullable"), producerNull, consumerNull, "Nullability is evaluated independently of logical type compatibility.");
    }

    private void Extra(ContractPath path, bool nativeClosed = false) => Add("SAT102", policy.RejectAdditionalElements || nativeClosed ? SatisfactionStatus.Unsatisfied : SatisfactionStatus.Informational,
        path, path.Value, null, "Additional producer element; the default policy permits extras.");
    private void Add(string code, SatisfactionStatus status, ContractPath path, object? offered, object? accepted, string explanation)
        => findings.Add(new(code, status, path.Value, path.Value, MetadataSnapshot.CopyValue(offered), MetadataSnapshot.CopyValue(accepted), explanation));
    internal static SatisfactionStatus Convert(CompatibilityClassification classification) => classification switch
    {
        CompatibilityClassification.NonBreaking => SatisfactionStatus.Satisfied,
        CompatibilityClassification.Breaking => SatisfactionStatus.Unsatisfied,
        _ => SatisfactionStatus.Unknown
    };
}
