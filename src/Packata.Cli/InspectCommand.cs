using System.ComponentModel;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class InspectSettings : ContractSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Inspect one canonical asset by identifier; omit to inspect all assets.")]
    public string? Asset { get; init; }
}

internal sealed class InspectCommand : AsyncCommand<InspectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InspectSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var source = await ContractSource.LoadAsync(settings.Source, settings.Format, cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            var contract = mapped.Value!;
            if (settings.Asset is not null)
            {
                var asset = ContractSelection.Asset(contract, settings.Asset);
                var endpointIds = asset.EndpointBindings.Select(binding => binding.EndpointId).ToHashSet(StringComparer.Ordinal);
                contract = contract with { Assets = [asset], Endpoints = contract.Endpoints.Where(endpoint => endpointIds.Contains(endpoint.Id)).ToArray() };
            }
            var result = CommandOutput.RedactedJson(new { schemaVersion = 1, source = settings.Source, contract, diagnostics = mapped.Diagnostics });
            if (settings.Output == "json") CommandOutput.Json(result);
            else
            {
                var tree = new Tree(new Text($"Contract: {CommandOutput.Redact(contract.Identity.Name ?? contract.Identity.Id)}"));
                AddProperties(tree.AddNode(new Text("Identity")), result["contract"]!["identity"]!);
                AddProperties(tree.AddNode(new Text("Metadata")), result["contract"]!["metadata"]!);
                AddProperties(tree.AddNode(new Text("Assets")), result["contract"]!["assets"]!);
                AddProperties(tree.AddNode(new Text("Endpoints")), result["contract"]!["endpoints"]!);
                AddProperties(tree.AddNode(new Text("Governance")), result["contract"]!["governance"]!);
                AnsiConsole.Write(tree);
                foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            }
            return mapped.IsSuccessful ? ExitCodes.Success : ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
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
