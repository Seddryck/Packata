using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
using Spectre.Console;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

internal static class StandaloneInferenceOutput
{
    // Normalize all file references, including resources other than the selected asset.
    // Archive members have no supported standalone locator: never silently detach them.
    internal static ContractDocument Prepare(ContractDocument document)
    {
        var source = document.Source!;
        if (source.Archive is null && source.BaseUri.IsFile) return document;
        var changed = false;
        string Resolve(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            {
                if (absolute.Scheme is not ("file" or "http" or "https"))
                    throw new NotSupportedException("Standalone output requires supported file, HTTP or HTTPS resource locators.");
                return path;
            }
            if (source.Archive is not null)
                throw new NotSupportedException("Standalone output cannot retain archive-relative resource references. Use display-only inference or descriptors with absolute resource locators.");
            changed = true;
            return new Uri(source.BaseUri, path).AbsoluteUri;
        }

        string content;
        if (document.Kind == ContractDocumentKind.DataPackageJson)
        {
            var root = JObject.Parse(document.OriginalText);
            foreach (var resource in (root["resources"] as JArray ?? []).OfType<JObject>())
            {
                if (resource["path"] is JValue { Type: JTokenType.String } value) value.Value = Resolve((string)value.Value!);
                else if (resource["path"] is JArray paths)
                    foreach (var path in paths.OfType<JValue>().Where(path => path.Type == JTokenType.String)) path.Value = Resolve((string)path.Value!);
            }
            content = root.ToString(Formatting.Indented);
        }
        else
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(document.OriginalText));
            var root = (YamlMappingNode)yaml.Documents[0].RootNode;
            var key = document.Kind == ContractDocumentKind.OpenDataContractYaml ? "servers" : "resources";
            if (root.Children.TryGetValue(new YamlScalarNode(key), out var nodes) && nodes is YamlSequenceNode sequence)
            {
                var fileIds = document.Contract.Endpoints.Where(endpoint => endpoint.Kind == EndpointKind.File).Select(endpoint => endpoint.Id).ToHashSet();
                foreach (var node in sequence.Children.OfType<YamlMappingNode>())
                {
                    if (key == "servers" && (!node.Children.TryGetValue(new YamlScalarNode("server"), out var id)
                        || !fileIds.Contains(((YamlScalarNode)id).Value!))) continue;
                    foreach (var field in key == "servers" ? new[] { "path", "location" } : new[] { "path" })
                    {
                        if (!node.Children.TryGetValue(new YamlScalarNode(field), out var value)) continue;
                        if (value is YamlScalarNode scalar && scalar.Value is not null) scalar.Value = Resolve(scalar.Value);
                        else if (value is YamlSequenceNode paths)
                            foreach (var path in paths.Children.OfType<YamlScalarNode>())
                                if (path.Value is not null) path.Value = Resolve(path.Value);
                    }
                }
            }
            using var writer = new StringWriter();
            yaml.Save(writer, assignAnchors: false);
            content = writer.ToString();
        }
        if (changed) AnsiConsole.MarkupLine("[yellow]CLI131:[/] Relative remote resource references were normalized to absolute URLs for standalone output.");
        return document with { OriginalText = content };
    }
}
