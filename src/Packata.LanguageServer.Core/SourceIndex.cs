using System.Text.RegularExpressions;
using Packata.Documents;
using YamlDotNet.RepresentationModel;

namespace Packata.LanguageServer.Core;

/// <summary>Indexes actual syntax nodes, never guesses line numbers from canonical names.</summary>
public sealed class SourceIndex(YamlMappingNode root)
{
    public YamlNode? Find(string pointer)
    {
        YamlNode? node = root;
        foreach (var segment in pointer.Split('/').Skip(1))
        {
            var name = segment.Replace("~1", "/").Replace("~0", "~");
            node = node switch
            {
                YamlMappingNode => ContractDocument.Child(node, name),
                YamlSequenceNode sequence when int.TryParse(name, out var index) && index >= 0 && index < sequence.Children.Count
                    => sequence.Children[index],
                YamlSequenceNode sequence => sequence.Children.FirstOrDefault(child =>
                    (ContractDocument.Child(child, "name") as YamlScalarNode)?.Value == name),
                _ => null
            };
        }
        return node;
    }

    public SourceRange? Range(string pointer)
    {
        var node = Find(pointer);
        if (node is null) return null;
        var end = End(node);
        return new(new((int)Math.Max(0, node.Start.Line - 1), (int)Math.Max(0, node.Start.Column - 1)),
            new((int)Math.Max(0, end.Line - 1), (int)Math.Max(0, end.Column - 1)));
    }

    // YamlDotNet representation collection nodes can have End == Start. Use their actual
    // descendant token marks; never guess a closing delimiter or scan text in the client.
    private static YamlDotNet.Core.Mark End(YamlNode node) => node switch
    {
        YamlMappingNode map when map.Children.Count > 0 => map.Children.SelectMany(pair => new[] { End(pair.Key), End(pair.Value) }).Append(node.End).MaxBy(mark => mark.Index),
        YamlSequenceNode sequence when sequence.Children.Count > 0 => sequence.Children.Select(End).Append(node.End).MaxBy(mark => mark.Index),
        _ => node.End
    };

    public SourceRange? DiagnosticRange(string path)
    {
        if (path == "$") return null;
        if (path.StartsWith("$."))
        {
            path = Regex.Replace(path[2..], @"\[(\d+)\]", "/$1").Replace('.', '/');
        }
        return Range("/" + path.TrimStart('/'));
    }

    public static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");
}
