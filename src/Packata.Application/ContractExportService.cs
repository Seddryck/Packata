using System.Text;
using Packata.Core.Contracts;
using Packata.DataPackage.Export;

namespace Packata.Application;

public sealed record ContractExportResult(string? Content, IReadOnlyList<MappingDiagnostic> Diagnostics)
{
    public bool IsSuccessful => Content is not null && Diagnostics.All(value => value.Severity != MappingSeverity.Error);
}

/// <summary>Maps a descriptor, selects its requested output and serializes it without writing files.</summary>
public static class ContractExportService
{
    public static ContractExportResult JsonSchema(ContractDocument document, string assetId, CancellationToken cancellationToken = default)
    {
        var mapped = ContractInspector.Inspect(document, cancellationToken).Mapping;
        if (!mapped.IsSuccessful) return new(null, mapped.Diagnostics);
        var asset = ContractInferenceService.SelectAsset(mapped.RequireValue(), assetId);
        using var stream = new MemoryStream();
        asset.ToJsonSchema().Serialize(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return new(Encoding.UTF8.GetString(stream.ToArray()), mapped.Diagnostics);
    }

    public static ContractExportResult Dialect(ContractDocument document, string endpointId, CancellationToken cancellationToken = default)
    {
        var mapped = ContractInspector.Inspect(document, cancellationToken).Mapping;
        if (!mapped.IsSuccessful) return new(null, mapped.Diagnostics);
        var result = new CsvDialectExporter().Export(mapped.RequireValue(), endpointId);
        cancellationToken.ThrowIfCancellationRequested();
        return new(result.Value, mapped.Diagnostics.Concat(result.Diagnostics).ToArray());
    }
}
