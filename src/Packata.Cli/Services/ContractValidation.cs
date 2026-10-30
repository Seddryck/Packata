namespace Packata.Cli;

internal static class ContractValidation
{
    internal static IReadOnlyList<Packata.Core.Contracts.MappingDiagnostic> Validate(ContractSource document)
        => Packata.Application.ContractDocument.Validate(document.NativeDocument);
}