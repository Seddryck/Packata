using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract.Mapping;
using YamlDotNet.RepresentationModel;
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
    public static ContractDocument Load(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The contract document does not exist.", fullPath);

        var text = File.ReadAllText(fullPath);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension == ".json")
        {
            var document = new Packata.DataPackage.DataPackageFactory().LoadFromFile(fullPath);
            var mapped = document.ToCanonicalContract();
            return new(fullPath, text, ContractDocumentKind.DataPackageJson,
                document, mapped.RequireValue(), mapped.Diagnostics);
        }

        if (extension is not (".yaml" or ".yml"))
            throw new NotSupportedException("Contract documents must use .json, .yaml, or .yml.");

        if (IsOpenDataContract(text))
        {
            var document = new Packata.OpenDataContract.DataContractFactory().LoadFromFile(fullPath);
            var mapped = document.ToCanonicalContract();
            return new(fullPath, text, ContractDocumentKind.OpenDataContractYaml,
                document, mapped.RequireValue(), mapped.Diagnostics);
        }

        var dataPackage = new Packata.DataPackage.DataPackageFactory().LoadFromFile(fullPath);
        var dataPackageMapping = dataPackage.ToCanonicalContract();
        return new(fullPath, text, ContractDocumentKind.DataPackageYaml,
            dataPackage, dataPackageMapping.RequireValue(), dataPackageMapping.Diagnostics);
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
                    $"Asset '{asset.Id}' must bind exactly one CSV path for inference.");
            return await resources[0].Paths[0].OpenAsync().ConfigureAwait(false);
        }

        if (NativeDocument is OdcsDocument)
        {
            if (endpoint.Location is not PathLocation { Paths.Count: 1 } location)
                throw new InvalidOperationException(
                    $"Asset '{asset.Id}' must bind exactly one local CSV path for inference.");
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

    private static bool IsOpenDataContract(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return false;
        return root.Children.TryGetValue(new YamlScalarNode("kind"), out var kind)
            && string.Equals(((YamlScalarNode)kind).Value, "DataContract", StringComparison.OrdinalIgnoreCase);
    }
}
