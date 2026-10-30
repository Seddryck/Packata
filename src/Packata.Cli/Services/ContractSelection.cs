using Packata.Core.Contracts;

namespace Packata.Cli;

internal sealed class ContractOperationException(string code, string path, string message) : Exception(message)
{
    public MappingDiagnostic Diagnostic { get; } = new(code, MappingSeverity.Error, path, message);
}

internal static class ContractSelection
{
    public static DataAsset Asset(DataContract contract, string id) => Packata.Application.ContractInferenceService.SelectAsset(contract, id);

    public static MappingResult<DataContract> Map(ContractSource source, CancellationToken cancellationToken = default)
    {
        var result = source.Map(cancellationToken);
        if (result.Value is null)
            throw new ContractOperationException("CLI012", "$", "The contract could not be mapped to the canonical model.");
        return result;
    }
}
