using Packata.Core.Contracts;
using YamlDotNet.RepresentationModel;

namespace Packata.Application;

public sealed record ComparisonPreparation(CompatibilityInput? Input, IReadOnlyList<MappingDiagnostic> ValidationDiagnostics,
    MappingResult<DataContract>? Mapping)
{
    public bool Valid => Input is not null;
}

/// <summary>Prepares validated native provenance and coordinates the three contract analyses.</summary>
public static class ContractComparisonService
{
    public static ComparisonPreparation Prepare(ContractDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = ContractDocument.Validate(document.NativeDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (validation.Any(value => value.Severity == MappingSeverity.Error)) return new(null, validation, null);
        var mapped = ContractInspector.Inspect(document, cancellationToken).Mapping;
        var input = Input(document, mapped);
        cancellationToken.ThrowIfCancellationRequested();
        return new(input, validation, mapped);
    }

    public static CompatibilityInput CreateInput(ContractDocument document, CancellationToken cancellationToken = default) =>
        Input(document, ContractInspector.Inspect(document, cancellationToken).Mapping);

    private static CompatibilityInput Input(ContractDocument document, MappingResult<DataContract> mapped) =>
        new(mapped.Value, document.Format == "datapackage" ? ContractStandard.DataPackage : ContractStandard.Odcs,
            mapped.Diagnostics, Tree(document.Root));

    public static ContractDiff Diff(DataContract baseline, DataContract candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = baseline.Diff(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static CompatibilityResult Compare(CompatibilityInput baseline, CompatibilityInput candidate, CancellationToken cancellationToken = default)
        => ContractCompatibility.Compare(baseline, candidate, cancellationToken);

    public static SatisfactionInput SatisfactionInput(CompatibilityInput input, string? source) =>
        new(input.Contract, input.Standard, source, input.Diagnostics, input.Descriptor);

    public static ContractSatisfaction Satisfies(SatisfactionInput producer, SatisfactionInput consumer,
        SatisfactionPolicy? policy = null, CancellationToken cancellationToken = default)
        => ContractSatisfactionEvaluator.EvaluateSatisfaction(producer, consumer, policy ?? SatisfactionPolicy.Default, cancellationToken);

    private static object? Tree(YamlNode node) => node switch
    {
        YamlMappingNode map => Tree(map),
        YamlSequenceNode sequence => sequence.Children.Select(Tree).ToArray(),
        YamlScalarNode scalar => scalar.Value,
        _ => null
    };

    private static IReadOnlyDictionary<string, object?> Tree(YamlMappingNode map) =>
        map.Children.ToDictionary(pair => ((YamlScalarNode)pair.Key).Value ?? string.Empty,
            pair => Tree(pair.Value), StringComparer.Ordinal);
}
