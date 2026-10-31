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
        AnsiConsole.MarkupLine($"[bold]Source:[/] {Markup.Escape(SafeText(Display(endpoint.Location)))}");

        var fields = new Table().Border(TableBorder.Rounded)
            .AddColumn("Field")
            .AddColumn("Logical type")
            .AddColumn("Format")
            .AddColumn("Physical type")
            .AddColumn("Nullable");
        foreach (var field in asset.Schema?.Fields ?? [])
            AddField(fields, field, field.Name);
        AnsiConsole.Write(fields);

        if (endpoint.Kind == EndpointKind.File && endpoint.Format is { Name: "csv" or "tsv" or "psv" } format)
        {
            AnsiConsole.MarkupLine(
                $"[bold]CSV dialect:[/] delimiter={Display(format.Options, "delimiter")}, " +
                $"header={Display(format.Options, "header")}, quote={Display(format.Options, "quoteChar")}, " +
                $"line terminator={Display(format.Options, "lineTerminator")}");
        }
        else if (endpoint.Kind == EndpointKind.File)
        {
            AnsiConsole.MarkupLine($"[bold]File format:[/] {Markup.Escape(endpoint.Format?.Name ?? "unknown")}");
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

    private static void AddField(Table table, DataField field, string path)
    {
        table.AddRow(Markup.Escape(path), Markup.Escape(field.LogicalType ?? "unknown"),
            Markup.Escape(field.Format ?? string.Empty), Markup.Escape(field.PhysicalType ?? string.Empty),
            field.Required ? "no" : "yes");
        foreach (var child in field.Children) AddField(table, child, $"{path}.{child.Name}");
        if (field.Items is { } item) AddField(table, item, $"{path}[]");
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

    internal static void WriteDiagnostic(string severity, string code, string message, double? confidence)
    {
        var color = CommandOutput.SeverityColor(severity);
        var confidenceText = confidence.HasValue ? $" (confidence {confidence:P0})" : string.Empty;
        AnsiConsole.MarkupLine(
            $"[{color}]{Markup.Escape(severity)} {Markup.Escape(code)}:[/] " +
            $"{Markup.Escape(SafeText(message + confidenceText))}");
    }

    internal static string SafeText(string text) => CommandOutput.Redact(System.Text.RegularExpressions.Regex.Replace(
        text, @"[^\s<>""']+[?#][^\s<>""']+", match =>
        {
            var value = match.Value;
            var end = value.IndexOfAny(['?', '#']);
            return end < 0 ? value : value[..end] + "[redacted]";
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
}
