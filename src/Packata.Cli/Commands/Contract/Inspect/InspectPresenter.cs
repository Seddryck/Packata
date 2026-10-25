using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace Packata.Cli;

internal static class InspectPresenter
{
    public static void Text(JToken result, IAnsiConsole console)
    {
        var identity = result["contract"]!["identity"]!;
        var name = (string?)identity["name"] ?? (string?)identity["id"] ?? string.Empty;
        var tree = new Tree(new Text($"Contract: {name}"));
        AddProperties(tree.AddNode(new Text("Identity")), result["contract"]!["identity"]!);
        AddProperties(tree.AddNode(new Text("Metadata")), result["contract"]!["metadata"]!);
        AddProperties(tree.AddNode(new Text("Assets")), result["contract"]!["assets"]!);
        AddProperties(tree.AddNode(new Text("Endpoints")), result["contract"]!["endpoints"]!);
        AddProperties(tree.AddNode(new Text("Governance")), result["contract"]!["governance"]!);
        console.Write(tree);
    }

    private static void AddProperties(TreeNode parent, JToken token)
    {
        if (token is JObject obj)
            AddObject(parent, obj);
        else if (token is JArray array)
            AddArray(parent, array);
    }

    private static void AddObject(TreeNode parent, JObject value)
    {
        foreach (var property in value.Properties())
        {
            if (property.Value.Type == JTokenType.Null) continue;
            if (property.Value is JContainer container)
            {
                if (container.HasValues) AddProperties(parent.AddNode(new Text(property.Name)), container);
            }
            else parent.AddNode(new Text($"{property.Name}: {property.Value}"));
        }
    }

    private static void AddArray(TreeNode parent, JArray array)
    {
        foreach (var item in array)
        {
            var label = item is JObject value ? (string?)value["id"] ?? (string?)value["name"] ?? "item" : item.ToString();
            var node = parent.AddNode(new Text(label));
            if (item is JContainer) AddProperties(node, item);
        }
    }
}
