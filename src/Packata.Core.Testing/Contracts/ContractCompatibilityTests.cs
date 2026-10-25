using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class ContractCompatibilityTests
{
    private static DataContract Contract(params DataField[] fields) => new(new("orders"), new(),
        [new("orders", "orders", null, null, AssetKind.Table, new(fields))], [], new());
    private static CompatibilityResult Compare(DataField before, DataField after) => ContractCompatibility.Compare(Contract(before), Contract(after));

    [Test]
    public void IdenticalContracts_DeclareDirectionPolicyAndCompletion()
    {
        var result = Compare(new("amount", "integer"), new("amount", "integer"));
        Assert.Multiple(() =>
        {
            Assert.That(result.Completed, Is.True);
            Assert.That(result.IsCompatible, Is.True);
            Assert.That(result.Policy, Is.EqualTo("baseline-consumers-v1"));
            Assert.That(result.Direction, Is.EqualTo("baseline-to-candidate"));
            Assert.That(result.Findings, Is.Empty);
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [TestCase("integer", "number", CompatibilityOutcome.Breaking)]
    [TestCase("number", "integer", CompatibilityOutcome.Compatible)]
    [TestCase("string", "integer", CompatibilityOutcome.Breaking)]
    [TestCase("datetime", "timestamp", CompatibilityOutcome.Compatible)]
    [TestCase("custom", "integer", CompatibilityOutcome.Unknown)]
    [TestCase(null, "integer", CompatibilityOutcome.Unknown)]
    public void LogicalTypes_AreDirectional(string? before, string? after, CompatibilityOutcome outcome)
    {
        var result = Compare(new("value", before), new("value", after));
        Assert.That(result.Outcome, Is.EqualTo(outcome));
        Assert.That(result.Findings.Single(x => x.Rule == "logical-type").Change, Is.TypeOf<PropertyTypeChanged>());
    }

    [Test]
    public void MixedPropertyModification_HasIndependentFacetsAndTypedEvidence()
    {
        var before = new DataField("id", "integer", Required: true, Nullable: false, Constraints: [new("maximum", 10)]);
        var after = before with { Required = false, Nullable = true, Constraints = [new("maximum", 5)], Format = "custom" };
        var result = Compare(before, after);
        Assert.That(result.Findings.Select(x => x.Classification), Does.Contain(CompatibilityClassification.Breaking)
            .And.Contain(CompatibilityClassification.NonBreaking).And.Contain(CompatibilityClassification.PotentiallyBreaking));
        Assert.That(result.Findings.All(x => x.Change is PropertyModified), Is.True);
        Assert.That(result.Findings.Select(x => x.Path.Value), Does.Contain("schema.orders.properties.id.constraints.maximum"));
        Assert.That(result.Findings.Select(x => x.Rule), Does.Contain("requiredness").And.Contain("nullability"));
        Assert.That(Compare(after, before).Findings.Single(x => x.Rule == "requiredness").Classification, Is.EqualTo(CompatibilityClassification.NonBreaking));
    }

    [TestCase("minimum", 1, 2, CompatibilityOutcome.Compatible)]
    [TestCase("minimum", 2, 1, CompatibilityOutcome.Breaking)]
    [TestCase("maximum", 2, 1, CompatibilityOutcome.Compatible)]
    [TestCase("maximum", 1, 2, CompatibilityOutcome.Breaking)]
    [TestCase("maxLength", 10, 5, CompatibilityOutcome.Compatible)]
    [TestCase("minLength", 5, 1, CompatibilityOutcome.Breaking)]
    [TestCase("minItems", 1, 2, CompatibilityOutcome.Compatible)]
    [TestCase("maxItems", 1, 2, CompatibilityOutcome.Breaking)]
    [TestCase("minProperties", 1, 2, CompatibilityOutcome.Compatible)]
    [TestCase("maxProperties", 1, 2, CompatibilityOutcome.Breaking)]
    public void BoundsAndLengths_CompareProducerGuarantees(string kind, int before, int after, CompatibilityOutcome outcome)
        => Assert.That(Compare(new("id", "integer", Constraints: [new(kind, before)]),
            new("id", "integer", Constraints: [new(kind, after)])).Outcome, Is.EqualTo(outcome));

    [Test]
    public void ExclusiveBounds_CompareEffectiveBoundsRatherThanIndependentRemovedConstraints()
    {
        var inclusive = new DataField("value", "number", Constraints: [new("minimum", 1)]);
        var exclusive = inclusive with { Constraints = [new("exclusiveMinimum", 1)] };
        Assert.That(Compare(inclusive, exclusive).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        Assert.That(Compare(exclusive, inclusive).Outcome, Is.EqualTo(CompatibilityOutcome.Breaking));
        var mixed = inclusive with { Constraints = [new("minimum", 0), new("exclusiveMinimum", 2)] };
        Assert.That(Compare(exclusive, mixed).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
    }

    [TestCase("minimum", 10, "exclusiveMinimum", 10, CompatibilityOutcome.Compatible)]
    [TestCase("exclusiveMinimum", 10, "minimum", 10, CompatibilityOutcome.Breaking)]
    [TestCase("maximum", 10, "exclusiveMaximum", 10, CompatibilityOutcome.Compatible)]
    [TestCase("exclusiveMaximum", 10, "maximum", 10, CompatibilityOutcome.Breaking)]
    [TestCase("exclusiveMinimum", 10, "minimum", 11, CompatibilityOutcome.Compatible)]
    [TestCase("minimum", 10, "exclusiveMinimum", 9, CompatibilityOutcome.Breaking)]
    [TestCase("exclusiveMaximum", 10, "maximum", 9, CompatibilityOutcome.Compatible)]
    [TestCase("maximum", 10, "exclusiveMaximum", 11, CompatibilityOutcome.Breaking)]
    [TestCase(null, 0, "minimum", 10, CompatibilityOutcome.Compatible)]
    [TestCase("minimum", 10, null, 0, CompatibilityOutcome.Breaking)]
    [TestCase(null, 0, "maximum", 10, CompatibilityOutcome.Compatible)]
    [TestCase("maximum", 10, null, 0, CompatibilityOutcome.Breaking)]
    public void NumericBounds_RespectDirectionPresenceAndExclusivity(string? beforeKind, int before,
        string? afterKind, int after, CompatibilityOutcome expected)
    {
        var baseline = new DataField("value", "number", Constraints: beforeKind is null ? [] : [new(beforeKind, before)]);
        var candidate = baseline with { Constraints = afterKind is null ? [] : [new(afterKind, after)] };
        var result = Compare(baseline, candidate);

        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Findings, Has.Count.EqualTo(1));
        Assert.That(result.Findings[0].Rule, Is.AnyOf("constraint:minimum", "constraint:maximum"));
    }

    [TestCase("minimum", "exclusiveMinimum", 10, 9)]
    [TestCase("maximum", "exclusiveMaximum", 10, 11)]
    public void CombinedNumericBounds_PreserveTheStrongerInclusiveGuarantee(string inclusive, string exclusive,
        int strong, int weak)
    {
        var baseline = new DataField("value", "number", Constraints: [new(inclusive, strong)]);
        var candidate = baseline with { Constraints = [new(inclusive, strong), new(exclusive, weak)] };

        Assert.That(Compare(baseline, candidate).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        Assert.That(Compare(candidate, baseline).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
    }

    [Test]
    public void DescriptiveMetadata_StillProducesANonBreakingFinding()
    {
        var baseline = Contract(new DataField("id", "integer"));
        var candidate = baseline with { Metadata = new(Description: "Updated description") };
        var result = ContractCompatibility.Compare(baseline, candidate);

        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("descriptive-metadata"));
    }

    [TestCase("unique")]
    [TestCase("uniqueItems")]
    [TestCase("primaryKey")]
    public void BooleanGuarantees_CanBeStrengthenedOrRemoved(string kind)
    {
        var before = new DataField("id", "integer");
        var after = before with { Constraints = [new(kind, true)] };
        Assert.That(Compare(before, after).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        Assert.That(Compare(after, before).Outcome, Is.EqualTo(CompatibilityOutcome.Breaking));
    }

    [Test]
    public void EnumSubset_IsSafeAndSupersetIsBreaking()
    {
        var before = new DataField("id", "integer", Constraints: [new("enum", new[] { 1, 2 })]);
        var after = before with { Constraints = [new("enum", new long[] { 1 })] };
        Assert.That(Compare(before, after).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        Assert.That(Compare(after, before).Outcome, Is.EqualTo(CompatibilityOutcome.Breaking));
        Assert.That(Compare(before, before with { Constraints = [] }).Outcome, Is.EqualTo(CompatibilityOutcome.Breaking));
    }

    [TestCase("pattern", "^a", "^b")]
    [TestCase("jsonSchema", "{}", "{unknown}")]
    [TestCase("minimum", "bad", "worse")]
    public void AmbiguousOrUnsupportedConstraints_AreUnknown(string kind, string before, string after)
    {
        var result = Compare(new("id", "string", Constraints: [new(kind, before)]), new("id", "string", Constraints: [new(kind, after)]));
        Assert.That(result.Completed, Is.True);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Unknown));
        Assert.That(result.IsCompatible, Is.False);
    }

    [Test]
    public void IdentifierChanges_RemainRemovalAdditionPairs()
    {
        var result = Compare(new("old", "integer"), new("new", "integer"));
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Breaking));
        Assert.That(result.Findings.Select(x => x.Change?.GetType()), Is.EquivalentTo(new[] { typeof(PropertyRemoved), typeof(PropertyAdded) }));
        var baseline = Contract();
        var candidate = baseline with { Assets = [baseline.Assets[0] with { Id = "new", Name = "new" }] };
        Assert.That(ContractCompatibility.Compare(baseline, candidate).Findings.Select(x => x.Change?.GetType()),
            Is.EquivalentTo(new[] { typeof(DatasetRemoved), typeof(DatasetAdded) }));
    }

    [Test]
    public void AdditionRemovalAndNestedFields_UseEscapedPathsAndDeterministicOrdering()
    {
        var baseline = Contract(new DataField("a.b", "object", Children: [new("old", "string")]));
        var candidate = Contract(new DataField("z", "integer"), new("a.b", "object", Children: [new("new", "string")]));
        var result = ContractCompatibility.Compare(baseline, candidate);
        Assert.That(result.Findings.Select(x => x.Path.Value), Is.Ordered.Using<string>(StringComparer.Ordinal));
        Assert.That(result.Findings.Select(x => x.Path.Value), Does.Contain("schema.orders.properties.a\\.b.properties.old"));
        Assert.That(ContractCompatibility.Compare(baseline, candidate).Findings.Select(x => (x.Path, x.Classification, x.Rule, x.Explanation)),
            Is.EqualTo(result.Findings.Select(x => (x.Path, x.Classification, x.Rule, x.Explanation))));
        Assert.That(ContractCompatibility.Compare(Contract(), Contract(new DataField("items", "integer"))).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
    }

    [Test]
    public void MappingWarningsAndMissingSourceCoverage_CannotPassAsCompatible()
    {
        var input = new CompatibilityInput(Contract(), ContractStandard.Odcs,
            [new("ODCS003", MappingSeverity.Warning, "relationships", "Unsupported reference")]);
        var result = ContractCompatibility.Compare(input, input);
        Assert.That(result.Completed, Is.True);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Unknown));
        Assert.That(result.Diagnostics.Select(x => x.Input), Is.EquivalentTo(new[] { "baseline", "candidate" }));
    }

    [TestCase(ContractStandard.Odcs)]
    [TestCase(ContractStandard.DataPackage)]
    public void UnmappedSourceMembers_ReportInputSpecificDiagnosticsAndPreventCompatibility(ContractStandard standard)
    {
        var contract = Contract(new DataField("id", "integer"));
        var baseline = new CompatibilityInput(contract, standard, Descriptor: new Dictionary<string, object?>());
        var candidate = baseline with { Descriptor = new Dictionary<string, object?> { ["unsupported"] = "value" } };
        var result = ContractCompatibility.Compare(baseline, candidate);

        Assert.That(result.Completed, Is.True);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Unknown));
        Assert.That(result.Diagnostics.Single(), Is.EqualTo(new CompatibilityDiagnostic("candidate", "CMP020",
            MappingSeverity.Warning, "$.unsupported", "Source semantics are not covered by this compatibility policy.")));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("candidate:source-coverage"));
    }

    [Test]
    public void DataPackageRequiredness_NormalizesNullabilityRecursively()
    {
        var field = new DataField("details", "object", Required: true, Nullable: false, Children:
            [new("values", "array", Required: true, Nullable: false, Items: new("element", "integer", Required: true, Nullable: false))]);
        var baseline = Contract(field);
        var child = field.Children[0];
        var candidate = Contract(field with
        {
            Nullable = true,
            Children = [child with { Nullable = true, Items = child.Items! with { Nullable = true } }]
        });
        var descriptor = new Dictionary<string, object?>();
        var result = ContractCompatibility.Compare(new CompatibilityInput(baseline, ContractStandard.DataPackage, Descriptor: descriptor),
            new CompatibilityInput(candidate, ContractStandard.DataPackage, Descriptor: descriptor));

        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        Assert.That(result.Findings, Is.Empty);
        var odcs = ContractCompatibility.Compare(new CompatibilityInput(baseline, ContractStandard.Odcs, Descriptor: descriptor),
            new CompatibilityInput(candidate, ContractStandard.Odcs, Descriptor: descriptor));
        Assert.That(odcs.Outcome, Is.EqualTo(CompatibilityOutcome.Breaking));
        Assert.That(odcs.Findings.Count(x => x.Rule == "nullability"), Is.EqualTo(3));
    }

    [TestCase(ContractStandard.Canonical)]
    [TestCase(ContractStandard.DataPackage)]
    public void MissingSchemas_PreventCompatibilityEvenWhenContractsAreIdentical(ContractStandard standard)
    {
        var contract = Contract();
        contract = contract with { Assets = [contract.Assets[0] with { Schema = null }] };
        var input = new CompatibilityInput(contract, standard, Descriptor: new Dictionary<string, object?>());
        var result = ContractCompatibility.Compare(input, input);

        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Unknown));
        Assert.That(result.Findings.Select(x => x.Rule), Is.EquivalentTo(new[] { "baseline:schema-coverage", "candidate:schema-coverage" }));
        Assert.That(result.Findings.Select(x => x.Path.Value), Is.All.EqualTo("schema.orders"));
    }

    [Test]
    public void DatasetAccessAndSchemaChanges_AreClassifiedIndependently()
    {
        var baseline = Contract(new DataField("id", "integer"));
        var asset = baseline.Assets[0];
        var access = baseline with { Assets = [asset with { PhysicalName = "orders_v2", Kind = AssetKind.View }] };
        var result = ContractCompatibility.Compare(baseline, access);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.PotentiallyBreaking));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("dataset-access"));

        var changed = access with { Assets = [access.Assets[0] with { Schema = asset.Schema! with { PrimaryKey = ["id"] } }] };
        result = ContractCompatibility.Compare(baseline, changed);
        Assert.That(result.Findings.Select(x => x.Rule), Is.EquivalentTo(new[] { "dataset-access", "dataset-semantics" }));
        Assert.That(result.Findings.Select(x => x.Classification), Is.EquivalentTo(new[]
        {
            CompatibilityClassification.PotentiallyBreaking, CompatibilityClassification.Unknown
        }));
    }

    [Test]
    public void IdentityAndGovernanceChanges_HaveDifferentCompatibilityImplications()
    {
        var baseline = Contract();
        var renamed = baseline with { Identity = baseline.Identity with { Name = "Sales" } };
        var result = ContractCompatibility.Compare(baseline, renamed);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.PotentiallyBreaking));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("contract-identity"));

        var versioned = baseline with { Identity = baseline.Identity with { Version = "2.0.0" } };
        Assert.That(ContractCompatibility.Compare(baseline, versioned).Findings.Single().Rule, Is.EqualTo("descriptive-metadata"));

        var governed = baseline with { Governance = new(Ownership: new("analytics")) };
        result = ContractCompatibility.Compare(baseline, governed);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Unknown));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("contract-metadata"));
    }

    [Test]
    public void ServerChanges_ArePotentiallyBreakingAndQualitySemanticsRemainUnknown()
    {
        var baseline = Contract(new DataField("id", "integer"));
        var candidate = baseline with { Endpoints = [new("files", "Local files", EndpointKind.File, "test", new PathLocation(["orders.csv"]))] };
        var result = ContractCompatibility.Compare(baseline, candidate);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.PotentiallyBreaking));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("server-access"));

        candidate = baseline with { Assets = [baseline.Assets[0] with { QualityRules = [new("custom", Expression: "id > 0")] }] };
        result = ContractCompatibility.Compare(baseline, candidate);
        Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Unknown));
        Assert.That(result.Findings.Single().Rule, Is.EqualTo("unsupported-change"));
    }

    [Test]
    public void FieldExtensions_RemainUnknownWhileRepresentationChangesArePotentiallyBreaking()
    {
        var baseline = new DataField("id", "integer");
        var candidate = baseline with
        {
            PhysicalName = "order_id", PhysicalType = "bigint",
            Extensions = ExtensionMetadata.For("custom", new Dictionary<string, object?> { ["semantics"] = "identity" })
        };
        var result = Compare(baseline, candidate);

        Assert.That(result.Findings.Select(x => x.Rule), Is.EquivalentTo(new[] { "physical-representation", "field-extensions" }));
        Assert.That(result.Findings.Select(x => x.Classification), Is.EquivalentTo(new[]
        {
            CompatibilityClassification.PotentiallyBreaking, CompatibilityClassification.Unknown
        }));
    }

    [Test]
    public void ArrayItemPaths_DoNotConfuseTraversalWithLiteralFieldNames()
    {
        foreach (var name in new[] { "properties", "items", "a.items", "a\\items" })
        {
            var before = new DataField(name, "array");
            var after = before with { Items = new("element", "integer") };
            Assert.That(Compare(before, after).Findings.Single().Rule, Is.EqualTo("items-shape"));
            Assert.That(ContractCompatibility.Compare(Contract(), Contract(before)).Outcome, Is.EqualTo(CompatibilityOutcome.Compatible));
        }
    }

    [Test]
    public void MissingModelsDuplicateIdentifiersAndConstraintKinds_FailAnalysis()
    {
        var valid = Contract(new DataField("id", "integer"));
        foreach (var invalid in new[]
        {
            new CompatibilityInput(null),
            new CompatibilityInput(Contract(new DataField("id", "integer"), new("id", "integer"))),
            new CompatibilityInput(Contract(new DataField("id", "integer", Constraints: [new("minimum", 1), new("minimum", 2)]))),
            new CompatibilityInput(valid, Diagnostics: [new("invalid", MappingSeverity.Error, "$", "Invalid")])
        })
        {
            var result = ContractCompatibility.Compare(invalid, new(valid));
            Assert.That(result.Completed, Is.False);
            Assert.That(result.IsCompatible, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(CompatibilityOutcome.Failed));
            Assert.That(result.Diagnostics.Any(x => x.Severity == MappingSeverity.Error), Is.True);
        }
    }

    [Test]
    public void UnhandledCanonicalFacetsAndUnsupportedValues_AreExplicit()
    {
        var contract = Contract();
        var changed = contract with { Lifecycle = new(EndOfLife: DateTimeOffset.Parse("2030-01-01T00:00:00Z")) };
        Assert.That(ContractCompatibility.Compare(contract, changed).Findings.Single().Classification, Is.EqualTo(CompatibilityClassification.Unknown));
        var unsupported = contract with { Extensions = ExtensionMetadata.For("custom", new Dictionary<string, object?> { ["value"] = new object() }) };
        Assert.That(ContractCompatibility.Compare(unsupported, contract).Completed, Is.False);
    }

    [Test]
    public void CrossStandardInputsAndCancellation_FailExplicitly()
    {
        var result = ContractCompatibility.Compare(new CompatibilityInput(Contract(), ContractStandard.Odcs), new CompatibilityInput(Contract(), ContractStandard.DataPackage));
        Assert.That(result.Completed, Is.False);
        Assert.That(result.Diagnostics.Select(x => x.Code), Does.Contain("CMP001"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        result = ContractCompatibility.Compare(Contract(), Contract(), cancellation.Token);
        Assert.That(result.Completed, Is.False);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("CMP130"));
    }
}
