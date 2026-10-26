using Packata.Core.Contracts;
using Packata.DataPackage.Validation;

namespace Packata.Cli;

internal static class ContractValidation
{
    internal static IReadOnlyList<MappingDiagnostic> Validate(ContractSource document)
    {
        if (document.NativeDocument is Packata.DataPackage.DataPackage package)
            return DataPackageValidator.Validate(package).Issues.Select(issue =>
                new MappingDiagnostic("DPV001", MappingSeverity.Error, issue.Path, issue.Message)).ToArray();

        var odcs = (Packata.OpenDataContract.DataContract)document.NativeDocument;
        var diagnostics = new List<MappingDiagnostic>();
        foreach (var (path, value) in new[] { ("id", odcs.Id), ("version", odcs.Version), ("status", odcs.Status) })
            if (string.IsNullOrWhiteSpace(value))
                diagnostics.Add(new("ODCSV001", MappingSeverity.Error, "$." + path, "A non-empty value is required."));
        if (odcs.Kind != "DataContract")
            diagnostics.Add(new("ODCSV002", MappingSeverity.Error, "$.kind", "kind must be DataContract."));
        if (odcs.ApiVersion != "v3.2.0")
            diagnostics.Add(new("ODCSV003", MappingSeverity.Error, "$.apiVersion", "Supported ODCS apiVersion is v3.2.0."));
        diagnostics.AddRange(new Packata.OpenDataContract.Mapping.OpenDataContractMapper().Map(odcs).Diagnostics
            .Where(diagnostic => diagnostic.Code is "ODCS006" or "ODCS007"));
        return diagnostics;
    }
}
