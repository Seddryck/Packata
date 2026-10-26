using System.Text;
using Packata.Core.Contracts;
using Packata.Core.Storage;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using Packata.OpenDataContract.Mapping;
using Packata.Storages;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

/// <summary>Loads descriptors without opening any referenced data or acquiring connectors.</summary>
internal sealed record ContractSource(string Source, Uri BaseUri, object NativeDocument) : IDisposable
{
    internal IDocumentContainerListable? Archive { get; init; }
    internal string? ArchiveDescriptor { get; init; }
    internal string OriginalText { get; init; } = string.Empty;
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

        // The operation owns its HTTP client; retained archives no longer need it after loading.
        using var http = uri.IsFile ? null : new HttpClient();
        using var cancellation = cancellationToken.Register(() => http?.CancelPendingRequests());
        return IsArchive(source)
            ? await LoadArchiveAsync(source, uri, http, format, cancellationToken, retainArchive).ConfigureAwait(false)
            : await LoadDocumentAsync(source, uri, http, format, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ContractSource> LoadDocumentAsync(string source, Uri uri, HttpClient? http,
        string format, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(uri, ".");
        using var httpContainer = uri.IsFile ? null : new HttpDataPackageContainer(baseUri, http, cancellationToken);
        await using var stream = uri.IsFile ? File.OpenRead(uri.LocalPath)
            : await httpContainer!.OpenAsync(uri.Segments[^1] + uri.Query).ConfigureAwait(false);
        var text = await ReadTextAsync(stream, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(source, new Uri(httpContainer?.ResponseUri ?? uri, "."), uri.AbsolutePath, text, format);
    }

    private static async Task<ContractSource> LoadArchiveAsync(string source, Uri uri, HttpClient? http,
        string format, CancellationToken cancellationToken, bool retainArchive)
    {
        var archive = await OpenArchiveAsync(uri, http, cancellationToken).ConfigureAwait(false);
        var retained = false;
        try
        {
            var descriptor = await FindArchiveDescriptorAsync(archive).ConfigureAwait(false);
            await using var entry = await archive.OpenAsync(descriptor).ConfigureAwait(false);
            var text = await ReadTextAsync(entry, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = Parse(source, new Uri(uri, "."), descriptor, text, format);
            if (retainArchive)
            {
                result = result with { Archive = archive, ArchiveDescriptor = descriptor };
                retained = true;
            }
            return result;
        }
        finally
        {
            if (!retained) archive.Dispose();
        }
    }
    private static async Task<IDocumentContainerListable> OpenArchiveAsync(Uri uri, HttpClient? http, CancellationToken cancellationToken)
    {
        var locator = new DataPackageLocatorBuilder()
            .Register("file", b => b.UseLocalFileSystem())
            .Register(new[] { "http", "https" }, b => b.UseHttp(http, cancellationToken))
            .Register("zip", b => b.UseZipArchive(cancellationToken))
            .Register("tar", b => b.UseTarArchive(cancellationToken: cancellationToken))
            .Register("tar.gz", b => b.UseTarArchive(ArchiveCompression.Gzip, cancellationToken: cancellationToken))
            .Register("tar.br", b => b.UseTarArchive(ArchiveCompression.Brotli, cancellationToken: cancellationToken)).Build();
        try
        {
            var handle = await locator.LocateAsync(uri).ConfigureAwait(false);
            return (IDocumentContainerListable)handle.Container;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The descriptor archive is truncated.", ex);
        }
    }

    private static async Task<string> FindArchiveDescriptorAsync(IDocumentContainerListable archive)
    {
        var candidates = (await archive.ListAsync().ConfigureAwait(false))
            .Where(IsSupportedDescriptor).ToArray();
        if (candidates.Length != 1)
            throw new ContractOperationException("CLI023", "$", "Archive must contain exactly one supported contract descriptor.");
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
        return new(source, baseUri, native) { DescriptorRoot = root, OriginalText = text };
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
