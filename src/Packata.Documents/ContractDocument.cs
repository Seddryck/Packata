using System.Text;
using Packata.Core.Contracts;
using Packata.Core.Storage;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using Packata.DataPackage.Validation;
using Packata.OpenDataContract.Mapping;
using YamlDotNet.RepresentationModel;

namespace Packata.Documents;

/// <summary>Shared descriptor parsing, mapping and validation for interactive and command-line hosts.</summary>
public sealed record ContractDocument(object NativeDocument, YamlMappingNode Root)
{
    public string Format => NativeDocument is Packata.DataPackage.DataPackage ? "datapackage" : "odcs";

    public static ContractDocument Parse(string text, string format = "auto", Uri? baseUri = null)
    {
        var root = ParseRoot(text);
        var odcs = format == "odcs" || (format == "auto" && Child(root, "kind") is not null);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var context = new DescriptorContainer(baseUri ?? new Uri(Path.GetFullPath(".") + Path.DirectorySeparatorChar));
        var serializationFormat = text.TrimStart().StartsWith('{') ? SerializationFormat.Json : SerializationFormat.Yaml;
        object native = odcs
            ? new Packata.OpenDataContract.DataContractFactory().LoadFromStream(content, context)
            : new Packata.DataPackage.DataPackageFactory().LoadFromStream(content, context, serializationFormat);
        return new(native, root);
    }

    /// <summary>Reads bounded source syntax independently of native model deserialization.</summary>
    public static YamlMappingNode ParseRoot(string text)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new FormatException("A contract must contain one mapping document.");
        var visited = 0;
        EnsureBounded(root, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance), 0, ref visited);
        return root;
    }

    /// <summary>Content-based recognition. File suffixes alone do not identify a contract.</summary>
    public static bool Recognizes(YamlMappingNode root) =>
        (Child(root, "kind") as YamlScalarNode)?.Value == "DataContract"
        || (Child(root, "apiVersion") as YamlScalarNode)?.Value?.StartsWith("v3.", StringComparison.Ordinal) == true
        || Child(root, "resources") is YamlSequenceNode
        || (Child(root, "profile") as YamlScalarNode)?.Value?.Contains("data-package", StringComparison.OrdinalIgnoreCase) == true;

    public static YamlNode? Child(YamlNode? node, string name) => node is YamlMappingNode map
        && map.Children.TryGetValue(new YamlScalarNode(name), out var child) ? child : null;

    private static void EnsureBounded(YamlNode node, HashSet<YamlNode> ancestors, int depth, ref int visited)
    {
        if (depth > 64 || ++visited > 100_000 || !ancestors.Add(node)) throw new FormatException("Descriptor expansion is too large, deep or cyclic.");
        if (node is YamlMappingNode map)
            foreach (var child in map.Children.Values) EnsureBounded(child, ancestors, depth + 1, ref visited);
        else if (node is YamlSequenceNode sequence)
            foreach (var child in sequence.Children) EnsureBounded(child, ancestors, depth + 1, ref visited);
        ancestors.Remove(node);
    }

    public MappingResult<DataContract> Map() => NativeDocument switch
    {
        Packata.DataPackage.DataPackage package => package.ToCanonicalContract(),
        Packata.OpenDataContract.DataContract contract => contract.ToCanonicalContract(),
        _ => throw new NotSupportedException()
    };

    public static IReadOnlyList<MappingDiagnostic> Validate(object native)
    {
        if (native is Packata.DataPackage.DataPackage package)
            return DataPackageValidator.Validate(package).Issues.Select(issue =>
                new MappingDiagnostic("DPV001", MappingSeverity.Error, issue.Path, issue.Message)).ToArray();
        var odcs = (Packata.OpenDataContract.DataContract)native;
        var diagnostics = new List<MappingDiagnostic>();
        foreach (var (path, value) in new[] { ("id", odcs.Id), ("version", odcs.Version), ("status", odcs.Status) })
            if (string.IsNullOrWhiteSpace(value))
                diagnostics.Add(new("ODCSV001", MappingSeverity.Error, "$." + path, "A non-empty value is required."));
        if (odcs.Kind != "DataContract")
            diagnostics.Add(new("ODCSV002", MappingSeverity.Error, "$.kind", "kind must be DataContract."));
        if (odcs.ApiVersion != "v3.2.0")
            diagnostics.Add(new("ODCSV003", MappingSeverity.Error, "$.apiVersion", "Supported ODCS apiVersion is v3.2.0."));
        diagnostics.AddRange(new OpenDataContractMapper().Map(odcs).Diagnostics
            .Where(diagnostic => diagnostic.Code is "ODCS006" or "ODCS007"));
        return diagnostics;
    }

    // Descriptor parsing never opens resource data. The retained context makes later explicit reads
    // of relative paths use the descriptor's original location, including unsaved buffers.
    private sealed class DescriptorContainer(Uri baseUri) : IDocumentContainer
    {
        public Uri BaseUri { get; } = baseUri;
        public Task<Stream> OpenAsync(string relativePath)
        {
            var uri = new Uri(BaseUri, relativePath);
            if (!uri.IsFile) throw new NotSupportedException("Use a storage provider for remote resource reads.");
            return Task.FromResult<Stream>(File.OpenRead(uri.LocalPath));
        }
        public Task<bool> ExistsAsync(string relativePath)
        {
            var uri = new Uri(BaseUri, relativePath);
            return Task.FromResult(uri.IsFile && File.Exists(uri.LocalPath));
        }
        public void Dispose() { }
    }
}
