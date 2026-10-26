using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
using Spectre.Console;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

internal static class StandaloneInferenceOutput
{
    internal static ContractDocument Prepare(ContractDocument document)
    {
        var source = document.Source ?? throw new InvalidOperationException("The descriptor source is unavailable.");
        if (source.Archive is null && source.BaseUri.IsFile) return document;
        var paths = new ResourceReferences(source);
        var content = document.Kind == ContractDocumentKind.DataPackageJson
            ? NormalizeJson(document.OriginalText, paths.Resolve)
            : NormalizeYaml(document, paths.Resolve);
        if (paths.Changed)
            AnsiConsole.MarkupLine("[yellow]CLI131:[/] Relative remote resource references were normalized to absolute URLs for standalone output.");
        return document with { OriginalText = content };
    }

    private static string NormalizeJson(string content, Func<string, string> resolve)
    {
        var root = JObject.Parse(content);
        foreach (var resource in (root["resources"] as JArray ?? []).OfType<JObject>())
            NormalizeJsonPath(resource["path"], resolve);
        return root.ToString(Formatting.Indented);
    }

    private static void NormalizeJsonPath(JToken? path, Func<string, string> resolve)
    {
        var values = path is JArray array ? array.OfType<JValue>() : new[] { path }.OfType<JValue>();
        foreach (var value in values.Where(value => value.Type == JTokenType.String))
            value.Value = resolve(value.Value<string>() ?? string.Empty);
    }

    private static string NormalizeYaml(ContractDocument document, Func<string, string> resolve)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(document.OriginalText));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        foreach (var node in FileReferences(root, document))
            NormalizeYamlReference(node, document.Kind, resolve);
        using var writer = new StringWriter();
        yaml.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static IEnumerable<YamlMappingNode> FileReferences(YamlMappingNode root, ContractDocument document)
    {
        var odcs = document.Kind == ContractDocumentKind.OpenDataContractYaml;
        var key = odcs ? "servers" : "resources";
        if (!root.Children.TryGetValue(new YamlScalarNode(key), out var nodes) || nodes is not YamlSequenceNode sequence)
            return [];
        var references = sequence.Children.OfType<YamlMappingNode>();
        if (!odcs) return references;
        var fileIds = document.Contract.Endpoints.Where(endpoint => endpoint.Kind == EndpointKind.File)
            .Select(endpoint => endpoint.Id).ToHashSet(StringComparer.Ordinal);
        return references.Where(node => node.Children.TryGetValue(new YamlScalarNode("server"), out var id)
            && id is YamlScalarNode scalar && scalar.Value is not null && fileIds.Contains(scalar.Value));
    }

    private static void NormalizeYamlReference(YamlMappingNode reference, ContractDocumentKind kind, Func<string, string> resolve)
    {
        var fields = kind == ContractDocumentKind.OpenDataContractYaml ? new[] { "path", "location" } : ["path"];
        foreach (var field in fields)
            if (reference.Children.TryGetValue(new YamlScalarNode(field), out var path)) NormalizeYamlPath(path, resolve);
    }

    private static void NormalizeYamlPath(YamlNode path, Func<string, string> resolve)
    {
        var values = path is YamlSequenceNode sequence ? sequence.Children.OfType<YamlScalarNode>() : new[] { path }.OfType<YamlScalarNode>();
        foreach (var value in values.Where(value => value.Value is not null))
            value.Value = resolve(value.Value ?? string.Empty);
    }

    private sealed class ResourceReferences(ContractSource source)
    {
        public bool Changed { get; private set; }
        public string Resolve(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            {
                if (absolute.Scheme is not ("file" or "http" or "https"))
                    throw new NotSupportedException("Standalone output requires supported file, HTTP or HTTPS resource locators.");
                return path;
            }
            if (source.Archive is not null)
                throw new NotSupportedException("Standalone output cannot retain archive-relative resource references. Use display-only inference or descriptors with absolute resource locators.");
            Changed = true;
            return new Uri(source.BaseUri, path).AbsoluteUri;
        }
    }
}
