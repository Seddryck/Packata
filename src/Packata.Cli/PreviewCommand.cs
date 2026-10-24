using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class PreviewSettings : CommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("ODCS YAML or Data Package v2 JSON/YAML contract document.")]
    public required string Source { get; init; }

    [CommandOption("--asset <ID>")]
    [Description("Required canonical asset identifier to preview.")]
    public required string Asset { get; init; }

    [CommandOption("--limit <COUNT>")]
    [Description("Maximum data rows to return, excluding headers. Must be positive.")]
    [DefaultValue(20)]
    public int Limit { get; init; } = 20;

    [CommandOption("--columns <NAMES>")]
    [Description("Comma-separated exact field names in the requested order; default is all fields.")]
    public string? Columns { get; init; }

    [CommandOption("--output <FORMAT>")]
    [Description("Output representation: table, json, or csv. Use shell redirection to save it.")]
    [DefaultValue("table")]
    public string Output { get; init; } = "table";

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        if (Limit <= 0) return ValidationResult.Error("--limit must be a positive integer.");
        if (Output is not ("table" or "json" or "csv")) return ValidationResult.Error("--output must be table, json, or csv.");
        if (Columns is not null)
        {
            var names = Columns.Split(',', StringSplitOptions.TrimEntries);
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                return ValidationResult.Error("--columns must contain non-empty, unique comma-separated field names.");
        }
        return ValidationResult.Success();
    }
}

internal sealed class PreviewCommand : AsyncCommand<PreviewSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PreviewSettings settings, CancellationToken cancellationToken)
    {
        DataEndpoint? endpoint = null;
        try
        {
            // ZIP descriptors are supported for validation/inspection. Data previews need an addressable resource base.
            if (Path.GetExtension(settings.Source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                throw new ContractOperationException("CLI023", "$", "Preview requires an unpacked ODCS or Data Package contract document.");
            var source = await ContractSource.LoadAsync(settings.Source, "auto", cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var asset = ContractSelection.Asset(mapped.Value!, settings.Asset);
            var request = PreviewService.Request(mapped.Value!, asset);
            endpoint = request.Endpoint;
            var sample = await PreviewService.ReadAsync(PreviewService.CreateFactory(source.BaseUri), request,
                settings.Limit, settings.Columns, cancellationToken).ConfigureAwait(false);
            Write(sample, settings.Output);
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            var diagnostic = CommandOutput.Failure(exception);
            if (endpoint is not null && exception is not (OperationCanceledException or ContractOperationException))
                diagnostic = diagnostic with { Message = $"Unable to preview asset '{settings.Asset}' at endpoint '{endpoint.Id}' (format '{endpoint.Format?.Name ?? "auto"}'). Check the reader provider, format, binding, and resource access." };
            if (exception is ReaderProviderUnavailableException unavailable)
                diagnostic = diagnostic with { Message = $"{diagnostic.Message} Required package: {unavailable.SuggestedPackage}; registration: {unavailable.RegistrationMethod}." };
            CommandOutput.Diagnostic(diagnostic);
            return CommandOutput.FailureExitCode(exception);
        }
    }

    internal static void Write(PreviewSample sample, string output, IAnsiConsole? console = null)
    {
        switch (output)
        {
            case "json":
                CommandOutput.Json(new { schemaVersion = 1, columns = sample.Columns, rows = sample.Rows });
                break;
            case "csv":
                Console.WriteLine(string.Join(',', sample.Columns.Select(Csv)));
                foreach (var row in sample.Rows) Console.WriteLine(string.Join(',', row.Select(value => Csv(Display(value)))));
                break;
            default:
                if (sample.Columns.Length == 0) { Console.WriteLine("No columns or rows."); break; }
                var table = new Table().Border(TableBorder.Rounded);
                foreach (var name in sample.Columns) table.AddColumn(new TableColumn(new Text(Cell(name))));
                foreach (var row in sample.Rows) table.AddRow(row.Select(value => new Text(Cell(value is null ? "(null)" : Display(value)))).ToArray());
                (console ?? AnsiConsole.Console).Write(table);
                Console.WriteLine($"{sample.Rows.Count} row(s).");
                break;
        }
    }

    private static string Cell(string value)
    {
        value = value.Replace("\r", "").Replace("\n", "↵").Replace("\t", " ");
        return value.Length > 80 ? value[..77] + "..." : value;
    }

    private static string Display(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        string text => text,
        _ => JsonConvert.SerializeObject(value)
    };

    private static string Csv(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? '"' + value.Replace("\"", "\"\"") + '"' : value;
}
