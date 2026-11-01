using Packata.Application;
using Packata.Application.Generation;

namespace Packata.LanguageServer.Core;

/// <summary>Projects existing syntax-backed tree locations, including ambiguous canonical paths.</summary>
internal sealed class GenerationLocations
{
    private readonly SourceIndex _index;
    private readonly Uri _source;
    private readonly Dictionary<string, SourceLocation> _fields;

    internal GenerationLocations(ContractDocument document, Uri source)
    {
        _source = source;
        _index = new(document.Root);
        var contract = document.Map().Value;
        var locations = new List<(string Path, SourceLocation Location)>();
        if (contract is not null)
        {
            var tree = new ContractTreeBuilder(document, source).Build(contract);
            var assets = tree.Children.SingleOrDefault(n => n.LogicalPath == "/assets")?.Children ?? [];
            foreach (var (asset, node) in contract.Assets.Zip(assets))
                foreach (var field in node.Children.Where(n => n.LogicalPath.EndsWith("/columns")).SelectMany(n => n.Children))
                    Collect(field, asset.Id + "/" + field.Label, locations);
        }
        _fields = locations.GroupBy(p => p.Path, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Location, StringComparer.Ordinal);
    }

    private static void Collect(ContractNode node, string path, List<(string Path, SourceLocation Location)> locations)
    {
        if (node.Location is { } location) locations.Add((path, location));
        foreach (var child in node.Children.Where(n => n.Kind == "field"))
            Collect(child, path + (child.SourcePath?.EndsWith("/items") == true ? "/items" : "/" + child.Label), locations);
    }

    internal SourceLocation? Find(GenerationDiagnostic diagnostic)
    {
        if (diagnostic.Template is { } template && diagnostic.Line is > 0 && diagnostic.Column is > 0
            && Uri.TryCreate(template, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var position = new Position(diagnostic.Line.Value - 1, diagnostic.Column.Value - 1);
            return new(uri.AbsoluteUri, new(position, position));
        }
        if (_fields.TryGetValue(diagnostic.SourcePath, out var location)) return location;
        var range = _index.DiagnosticRange(diagnostic.SourcePath);
        return range is null ? null : new(_source.AbsoluteUri, range);
    }
}
