using Packata.Core.Contracts;
using Spectre.Console;

namespace Packata.Cli;

internal static class InferencePresenter
{
    public static void Write(
        IReadOnlyList<MappingDiagnostic> mappingDiagnostics,
        DataContractInferenceResult result,
        DataAsset asset,
        DataEndpoint endpoint)
    {
        AnsiConsole.MarkupLine($"[bold]Inferred asset:[/] {Markup.Escape(asset.Id)}");
        AnsiConsole.MarkupLine($"[bold]Source:[/] {Markup.Escape(Display(endpoint.Location))}");

        var fields = new Table().Border(TableBorder.Rounded)
            .AddColumn("Field")
            .AddColumn("Logical type")
            .AddColumn("Format")
            .AddColumn("Nullable");
        foreach (var field in asset.Schema?.Fields ?? [])
            fields.AddRow(
                Markup.Escape(field.Name),
                Markup.Escape(field.LogicalType ?? "unknown"),
                Markup.Escape(field.Format ?? string.Empty),
                field.Required ? "no" : "yes");
        AnsiConsole.Write(fields);

        if (endpoint.Kind == EndpointKind.File && endpoint.Format is { } format)
        {
            AnsiConsole.MarkupLine(
                $"[bold]CSV dialect:[/] delimiter={Display(format.Options, "delimiter")}, " +
                $"header={Display(format.Options, "header")}, quote={Display(format.Options, "quoteChar")}, " +
                $"line terminator={Display(format.Options, "lineTerminator")}");
        }
        else if (endpoint.Kind == EndpointKind.Database)
        {
            AnsiConsole.MarkupLine(
                $"[bold]Database object:[/] {Markup.Escape(asset.PhysicalName ?? asset.Name)}");
            if (asset.Schema?.PrimaryKey.Count > 0)
                AnsiConsole.MarkupLine(
                    $"[bold]Primary key:[/] {Markup.Escape(string.Join(", ", asset.Schema.PrimaryKey))}");
            foreach (var relationship in asset.Schema?.Relationships ?? [])
            {
                AnsiConsole.MarkupLine(
                    $"[bold]Relationship:[/] {Markup.Escape(string.Join(", ", relationship.Fields))} " +
                    $"→ {Markup.Escape(relationship.TargetAsset)}" +
                    $"({Markup.Escape(string.Join(", ", relationship.TargetFields))})");
            }
        }

        foreach (var diagnostic in mappingDiagnostics)
            WriteDiagnostic(diagnostic.Severity.ToString(), diagnostic.Code, diagnostic.Message, null);
        foreach (var diagnostic in result.Diagnostics)
            WriteDiagnostic(diagnostic.Severity.ToString(), diagnostic.Code, diagnostic.Message, diagnostic.Confidence);
    }

    private static string Display(DataLocation location) => location switch
    {
        PathLocation path => path.Paths.Single(),
        ConnectionLocation connection when connection.ConnectionUrl is not null => connection.ConnectionUrl,
        ConnectionLocation connection =>
            $"{connection.Scheme}://{connection.Host ?? "."}/{connection.Database ?? string.Empty}",
        _ => location.ToString() ?? "unknown"
    };

    private static string Display(IReadOnlyDictionary<string, object?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || value is null) return "none";
        return Markup.Escape(value.ToString()!.Replace("\r", "\\r").Replace("\n", "\\n"));
    }

    private static void WriteDiagnostic(string severity, string code, string message, double? confidence)
    {
        var color = severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? "yellow" : "grey";
        var confidenceText = confidence.HasValue ? $" (confidence {confidence:P0})" : string.Empty;
        AnsiConsole.MarkupLine(
            $"[{color}]{Markup.Escape(severity)} {Markup.Escape(code)}:[/] " +
            $"{Markup.Escape(message + confidenceText)}");
    }
}
