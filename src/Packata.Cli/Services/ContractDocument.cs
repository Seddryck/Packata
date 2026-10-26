using Packata.Core.Contracts;
using CanonicalContract = Packata.Core.Contracts.DataContract;
using DataPackageDocument = Packata.DataPackage.DataPackage;
using OdcsDocument = Packata.OpenDataContract.DataContract;

namespace Packata.Cli;

internal enum ContractDocumentKind
{
    DataPackageJson,
    DataPackageYaml,
    OpenDataContractYaml
}

internal sealed record ContractDocument(
    string SourcePath,
    string OriginalText,
    ContractDocumentKind Kind,
    object NativeDocument,
    CanonicalContract Contract,
    IReadOnlyList<MappingDiagnostic> MappingDiagnostics)
{
    public static ContractDocument Load(string sourcePath, string inputFormat = "auto")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (ContractSource.IsArchive(sourcePath))
            throw new NotSupportedException("Schema inference requires an unpacked local contract document.");
        var fullPath = Path.GetFullPath(sourcePath);
        var text = File.ReadAllText(fullPath);
        var source = ContractSource.Parse(sourcePath, new Uri(Path.GetDirectoryName(fullPath)! + Path.DirectorySeparatorChar),
            fullPath, text, inputFormat);
        var mapped = source.Map();
        var kind = source.NativeDocument is OdcsDocument ? ContractDocumentKind.OpenDataContractYaml
            : text.TrimStart().StartsWith('{') ? ContractDocumentKind.DataPackageJson : ContractDocumentKind.DataPackageYaml;
        return new(fullPath, text, kind, source.NativeDocument, mapped.RequireValue(), mapped.Diagnostics);
    }

    public async ValueTask<Stream> OpenAssetStreamAsync(DataAsset asset, DataEndpoint endpoint)
    {
        if (NativeDocument is DataPackageDocument package)
        {
            var resources = package.Resources.Where(resource =>
                string.Equals(resource.Name, asset.Id, StringComparison.Ordinal)).ToArray();
            if (resources.Length != 1)
                throw new InvalidOperationException(
                    $"Asset '{asset.Id}' does not identify exactly one Data Package resource.");
            if (resources[0].Paths.Count != 1)
                throw new InvalidOperationException(
                    $"Asset '{asset.Id}' must bind exactly one file path for inference.");
            var resourcePath = resources[0].Paths[0];
            return resourcePath.IsFullyQualified
                ? await resourcePath.OpenAsync().ConfigureAwait(false)
                : File.OpenRead(Path.GetFullPath(resourcePath.Value, Path.GetDirectoryName(SourcePath)!));
        }

        if (NativeDocument is OdcsDocument)
        {
            if (endpoint.Location is not PathLocation { Paths.Count: 1 } location)
                throw new InvalidOperationException(
                    $"Asset '{asset.Id}' must bind exactly one local file path for inference.");
            var path = location.Paths[0];
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile)
                    throw new NotSupportedException("ODCS inference currently supports local file endpoints only.");
                path = uri.LocalPath;
            }
            else if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(Path.GetDirectoryName(SourcePath)!, path);
            }
            return File.OpenRead(path);
        }

        throw new NotSupportedException("Unsupported contract document type.");
    }

}
