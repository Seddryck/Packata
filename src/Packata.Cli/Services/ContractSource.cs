using System.Text;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using Packata.OpenDataContract.Mapping;
using Packata.Storages;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

/// <summary>Loads descriptors without opening any referenced data or acquiring connectors.</summary>
internal sealed record ContractSource(string Source, Uri BaseUri, object NativeDocument)
{
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

    public static async Task<ContractSource> LoadAsync(string source, string format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = Uri.TryCreate(source, UriKind.Absolute, out var absolute) ? absolute : new Uri(Path.GetFullPath(source));
        if (uri.Scheme is not ("file" or "http" or "https"))
            throw new NotSupportedException("Unsupported source scheme.");

        // The operation owns its HTTP client so cancellation also aborts opening through the storage abstraction.
        using var http = uri.IsFile ? null : new HttpClient();
        using var cancellation = cancellationToken.Register(() => http?.CancelPendingRequests());
        using var httpContainer = uri.IsFile ? null : new HttpDataPackageContainer(new Uri(uri, "."), http);
        await using var stream = uri.IsFile
            ? File.OpenRead(uri.LocalPath)
            : await httpContainer!.OpenAsync(uri.Segments[^1] + uri.Query).ConfigureAwait(false);
        var descriptor = uri.AbsolutePath;
        var baseUri = new Uri(uri, ".");
        string text;
        if (Path.GetExtension(descriptor).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var container = new ZipDataPackageContainer(uri, stream);
            var candidates = (await container.ListFilesAsync().ConfigureAwait(false))
                .Where(name => Path.GetFileName(name).ToLowerInvariant() is "datapackage.json" or "datapackage.yaml" or "datapackage.yml" or "contract.yaml" or "contract.yml")
                .ToArray();
            if (candidates.Length != 1)
                throw new InvalidDataException("ZIP must contain exactly one supported contract descriptor.");
            descriptor = candidates[0];
            await using var entry = await container.OpenAsync(descriptor).ConfigureAwait(false);
            using var reader = new StreamReader(entry);
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(source, baseUri, descriptor, text, format);
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
