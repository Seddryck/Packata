using System.Text;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using Packata.OpenDataContract.Mapping;
using Packata.Storages;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

/// <summary>Loads descriptors without opening any referenced data or acquiring connectors.</summary>
internal sealed record ContractSource(string Source, Uri BaseUri, object NativeDocument) : IDisposable
{
    internal ZipDataPackageContainer? Archive { get; init; }
    internal string? ArchiveDescriptor { get; init; }
    public void Dispose() => Archive?.Dispose();

    internal static bool IsArchive(string source)
    {
        var path = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.AbsolutePath : source;
        return new[] { ".zip", ".tar", ".tar.gz", ".tar.br" }
            .Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
    internal YamlMappingNode? DescriptorRoot { get; init; }

    internal CompatibilityInput CompatibilityInput()
    {
        var mapped = Map();
        return new(mapped.Value, NativeDocument is Packata.DataPackage.DataPackage
            ? ContractStandard.DataPackage : ContractStandard.Odcs, mapped.Diagnostics,
            DescriptorRoot is null ? null : Tree(DescriptorRoot));
    }

    public MappingResult<DataContract> Map() => NativeDocument switch
    {
        Packata.DataPackage.DataPackage package => package.ToCanonicalContract(),
        Packata.OpenDataContract.DataContract contract => contract.ToCanonicalContract(),
        _ => throw new NotSupportedException()
    };

    public static async Task<ContractSource> LoadAsync(string source, string format, CancellationToken cancellationToken, bool retainArchive = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = Uri.TryCreate(source, UriKind.Absolute, out var absolute) ? absolute : new Uri(Path.GetFullPath(source));
        if (uri.Scheme is not ("file" or "http" or "https"))
            throw new NotSupportedException("Unsupported source scheme.");

        // The operation owns its HTTP client so cancellation also aborts opening through the storage abstraction.
        using var http = uri.IsFile ? null : new HttpClient();
        using var cancellation = cancellationToken.Register(() => http?.CancelPendingRequests());
        if (IsArchive(source) && !uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return await LoadTarDescriptorAsync(source, uri, format, http, cancellationToken).ConfigureAwait(false);
        using var httpContainer = uri.IsFile ? null : new HttpDataPackageContainer(new Uri(uri, "."), http);
        var stream = uri.IsFile
            ? File.OpenRead(uri.LocalPath)
            : await httpContainer!.OpenAsync(uri.Segments[^1] + uri.Query).ConfigureAwait(false);
        return await LoadStreamAsync(source, uri, stream, format, cancellationToken, retainArchive).ConfigureAwait(false);
    }

    private static async Task<ContractSource> LoadTarDescriptorAsync(string source, Uri uri, string format,
        HttpClient? http, CancellationToken cancellationToken)
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Register(new[] { "http", "https" }, b => b.UseHttp(http))
            .Register("tar", b => b.UseTarArchive())
            .Register("tar.gz", b => b.UseTarArchive(ArchiveCompression.Gzip))
            .Register("tar.br", b => b.UseTarArchive(ArchiveCompression.Brotli)).Build();
        Packata.Core.Storage.DocumentHandle handle;
        try { handle = await locator.LocateAsync(uri).ConfigureAwait(false); }
        catch (EndOfStreamException ex) { throw new InvalidDataException("The descriptor archive is truncated.", ex); }
        using var container = handle.Container;
        var candidates = (await ((Packata.Core.Storage.IDocumentContainerListable)container).ListAsync().ConfigureAwait(false))
            .Where(IsSupportedDescriptor).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("Archive must contain exactly one supported contract descriptor.");
        await using var entry = await container.OpenAsync(candidates[0]).ConfigureAwait(false);
        var text = await ReadTextAsync(entry, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(source, new Uri(uri, "."), candidates[0], text, format);
    }
    private static async Task<ContractSource> LoadStreamAsync(string source, Uri uri, Stream stream,
        string format, CancellationToken cancellationToken, bool retainArchive)
    {
        ZipDataPackageContainer? archive = null;
        var retained = false;
        try
        {
            var descriptor = uri.AbsolutePath;
            var baseUri = new Uri(uri, ".");
            string text;
            if (Path.GetExtension(descriptor).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                archive = new ZipDataPackageContainer(uri, stream);
                descriptor = await FindArchiveDescriptorAsync(archive).ConfigureAwait(false);
                await using var entry = await archive.OpenAsync(descriptor).ConfigureAwait(false);
                text = await ReadTextAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                text = await ReadTextAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var result = Parse(source, baseUri, descriptor, text, format);
            if (retainArchive && archive is not null)
            {
                result = result with { Archive = archive, ArchiveDescriptor = descriptor };
                retained = true;
            }
            return result;
        }
        finally
        {
            if (!retained)
            {
                archive?.Dispose();
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> FindArchiveDescriptorAsync(ZipDataPackageContainer archive)
    {
        var candidates = (await archive.ListFilesAsync().ConfigureAwait(false))
            .Where(IsSupportedDescriptor).ToArray();
        if (candidates.Length != 1)
            throw new ContractOperationException("CLI023", "$", "ZIP must contain exactly one supported contract descriptor.");
        return candidates[0];
    }

    private static bool IsSupportedDescriptor(string name)
        => Path.GetFileName(name).ToLowerInvariant() is "datapackage.json" or "datapackage.yaml"
            or "datapackage.yml" or "contract.yaml" or "contract.yml";

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static ContractSource Parse(string source, Uri baseUri, string descriptor, string text, string format)
    {
        // JSON is also YAML; inspecting the root avoids treating an extension as a standard identifier.
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new FormatException("A contract must contain one mapping document.");
        var odcs = format == "odcs" || (format == "auto" &&
            root.Children.ContainsKey(new YamlScalarNode("kind")));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        object native = odcs
            ? new Packata.OpenDataContract.DataContractFactory().LoadFromStream(content)
            : new Packata.DataPackage.DataPackageFactory().LoadFromStream(content,
                text.TrimStart().StartsWith('{') ? SerializationFormat.Json : SerializationFormat.Yaml);
        return new(source, baseUri, native) { DescriptorRoot = root };
    }

    // Transport-only projection; source coverage and all compatibility rules live in Core.
    private static object? Tree(YamlNode node) => node switch
    {
        YamlMappingNode map => Tree(map),
        YamlSequenceNode sequence => sequence.Children.Select(Tree).ToArray(),
        YamlScalarNode scalar => scalar.Value,
        _ => null
    };

    private static IReadOnlyDictionary<string, object?> Tree(YamlMappingNode map)
        => map.Children.ToDictionary(pair => ((YamlScalarNode)pair.Key).Value ?? string.Empty,
            pair => Tree(pair.Value), StringComparer.Ordinal);
}
