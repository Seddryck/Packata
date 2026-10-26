using Packata.Core.Contracts;
using Packata.Storages;
using CanonicalContract = Packata.Core.Contracts.DataContract;
using OdcsDocument = Packata.OpenDataContract.DataContract;

namespace Packata.Cli;

internal enum ContractDocumentKind { DataPackageJson, DataPackageYaml, OpenDataContractYaml }

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
            var kind = source.NativeDocument is OdcsDocument ? ContractDocumentKind.OpenDataContractYaml
                : source.OriginalText.TrimStart().StartsWith('{') ? ContractDocumentKind.DataPackageJson : ContractDocumentKind.DataPackageYaml;
            var uri = Uri.TryCreate(sourcePath, UriKind.Absolute, out var absolute) ? absolute : new Uri(Path.GetFullPath(sourcePath));
            return new(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri, source.OriginalText, kind,
                source.NativeDocument, mapped.RequireValue(), mapped.Diagnostics) { Source = source };
        }
        catch { source.Dispose(); throw; }
    }

    public async ValueTask<Stream> OpenAssetStreamAsync(DataAsset asset, DataEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        if (endpoint.Location is not PathLocation { Paths.Count: 1 } location)
            throw new InvalidOperationException($"Asset '{asset.Id}' must bind exactly one file path for inference.");
        var source = Source ?? throw new InvalidOperationException("The descriptor source is unavailable.");
        var resolver = source.Archive is null
            ? (Packata.Core.Reading.IEndpointStreamResolver)new PreviewService.RelativeStreamResolver(source.BaseUri)
            : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor!);
        var stream = await resolver.OpenAsync(location.Paths[0], cancellationToken).ConfigureAwait(false);
        return stream.CanSeek ? stream : new BoundedReadStream(stream, SeekableStaging.DefaultMaximumBytes, cancellationToken);
    }
}
