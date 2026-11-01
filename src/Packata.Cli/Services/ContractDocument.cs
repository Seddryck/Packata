using Packata.Core.Contracts;
using Packata.Storages;
using Packata.ResourceReaders;
using CanonicalContract = Packata.Core.Contracts.DataContract;
using OdcsDocument = Packata.OpenDataContract.DataContract;

namespace Packata.Cli;

internal enum ContractDocumentKind { DataPackageJson,
    DataPackageYaml,
    OpenDataContractYaml }

internal sealed record ContractDocument(
    string SourcePath,
    string OriginalText,
    ContractDocumentKind Kind,
    object NativeDocument,
    CanonicalContract Contract,
    IReadOnlyList<MappingDiagnostic> MappingDiagnostics) : IDisposable
{
    internal ContractSource? Source { get; init; }
    public void Dispose() => Source?.Dispose();

    public static ContractDocument Load(string sourcePath, string inputFormat = "auto") =>
        LoadAsync(sourcePath, inputFormat, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<ContractDocument> LoadAsync(string sourcePath, string inputFormat, CancellationToken cancellationToken)
    {
        var source = await ContractSource.LoadAsync(sourcePath, inputFormat, cancellationToken, retainArchive: true).ConfigureAwait(false);
        try
        {
            var mapped = source.Map();
            var kind = source.OriginalText.TrimStart().StartsWith('{') ? ContractDocumentKind.DataPackageJson : ContractDocumentKind.DataPackageYaml;
            if (source.NativeDocument is OdcsDocument) kind = ContractDocumentKind.OpenDataContractYaml;
            var uri = Uri.TryCreate(sourcePath, UriKind.Absolute, out var absolute) ? absolute : new Uri(Path.GetFullPath(sourcePath));
            return new(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri, source.OriginalText, kind,
                source.NativeDocument, mapped.RequireValue(), mapped.Diagnostics) { Source = source };
        }
        catch { source.Dispose(); throw; }
    }

    public async ValueTask<Stream> OpenAssetStreamAsync(DataAsset asset, DataEndpoint endpoint,
        CancellationToken cancellationToken = default, long stagingMaximumBytes = SeekableStaging.DefaultMaximumBytes,
        long? workbookMaximumBytes = null)
    {
        if (endpoint.Location is not PathLocation { Paths.Count: 1 } location)
            throw new InvalidOperationException($"Asset '{asset.Id}' must bind exactly one file path for inference.");
        var source = Source ?? throw new InvalidOperationException("The descriptor source is unavailable.");
        var resolver = source.Archive is null
            ? (Packata.Core.Reading.IEndpointStreamResolver)new PreviewService.RelativeStreamResolver(source.BaseUri)
            : new PreviewService.ArchiveStreamResolver(source.Archive,
                source.ArchiveDescriptor ?? throw new InvalidOperationException("The archive descriptor is unavailable."));
        var acquisition = new ResourceAcquisition(resolver, new DataFormatResolver().Resolve(endpoint), stagingMaximumBytes, workbookMaximumBytes);
        return await acquisition.OpenAsync(location.Paths[0], cancellationToken).ConfigureAwait(false);
    }
}
