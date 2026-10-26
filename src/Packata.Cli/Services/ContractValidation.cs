namespace Packata.Cli;

internal static class ContractValidation
{
    internal static IReadOnlyList<Packata.Core.Contracts.MappingDiagnostic> Validate(ContractSource document)
        => Packata.Documents.ContractDocument.Validate(document.NativeDocument);
}