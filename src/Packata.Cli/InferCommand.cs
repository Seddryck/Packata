using System.Globalization;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using PocketCsvReader;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class InferCommand : AsyncCommand<InferSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        InferSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = ContractDocument.Load(settings.Source);
            var asset = SelectAsset(document.Contract, settings.Asset);
            var endpoint = SelectEndpoint(document.Contract, asset);
            ValidateEndpoint(asset, endpoint);

            await using var stream = await document.OpenAssetStreamAsync(asset, endpoint).ConfigureAwait(false);
            var inferenceOptions = CreateOptions(settings, endpoint);
            var options = new CsvDataContractInferenceOptions
            {
                InferenceOptions = inferenceOptions,
                ContractIdentity = document.Contract.Identity,
                ContractMetadata = document.Contract.Metadata,
                AssetId = asset.Id,
                AssetName = asset.Name,
                AssetPhysicalName = asset.PhysicalName,
                AssetDescription = asset.Description,
                EndpointId = endpoint.Id,
                EndpointName = endpoint.Name
            };
            var sourcePath = ((PathLocation)endpoint.Location).Paths.Single();
            var result = new CsvDataContractInference(options).Infer(stream, sourcePath);

            InferencePresenter.Write(document.MappingDiagnostics, result);

            if (settings.Write || settings.Output is not null)
            {
                var update = ContractDocumentUpdater.Update(document, asset, endpoint, result.Contract.Assets.Single(),
                    result.Contract.Endpoints.Single());
                foreach (var warning in update.Warnings)
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
                var target = settings.Write ? document.SourcePath : Path.GetFullPath(settings.Output!);
                ContractDocumentWriter.Write(target, update.Content, overwrite: settings.Write);
                AnsiConsole.MarkupLine($"[green]Written:[/] {Markup.Escape(target)}");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Inference was cancelled.[/]");
            return 2;
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            return 1;
        }
    }

    private static DataAsset SelectAsset(DataContract contract, string assetId)
    {
        var matches = contract.Assets.Where(asset =>
            string.Equals(asset.Id, assetId, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Asset '{assetId}' was not found."),
            _ => throw new InvalidOperationException($"Asset identifier '{assetId}' is ambiguous.")
        };
    }

    private static DataEndpoint SelectEndpoint(DataContract contract, DataAsset asset)
    {
        if (asset.EndpointBindings.Count != 1)
            throw new InvalidOperationException(
                $"Asset '{asset.Id}' must have exactly one endpoint binding for inference.");
        var endpointId = asset.EndpointBindings[0].EndpointId;
        var matches = contract.Endpoints.Where(endpoint => endpoint.Id == endpointId).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Endpoint binding '{endpointId}' for asset '{asset.Id}' could not be resolved uniquely.");
    }

    private static void ValidateEndpoint(DataAsset asset, DataEndpoint endpoint)
    {
        if (endpoint.Kind != EndpointKind.File || endpoint.Location is not PathLocation { Paths.Count: 1 })
            throw new NotSupportedException(
                $"Asset '{asset.Id}' must bind exactly one file endpoint for CSV inference.");

        var name = endpoint.Format?.Name?.Trim().TrimStart('.').ToLowerInvariant();
        var pathExtension = Path.GetExtension(((PathLocation)endpoint.Location).Paths[0])
            .TrimStart('.').ToLowerInvariant();
        if (name is not null && name is not ("csv" or "tsv" or "psv"))
            throw new NotSupportedException($"Asset '{asset.Id}' is not bound to a supported CSV resource.");
        if (name is null && pathExtension is not ("csv" or "tsv" or "psv"))
            throw new NotSupportedException($"Asset '{asset.Id}' is not bound to a supported CSV resource.");
    }

    private static CsvInferenceOptions CreateOptions(InferSettings settings, DataEndpoint endpoint)
    {
        var format = endpoint.Format;
        var defaultDelimiter = format?.Name?.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "tsv" => '\t',
            "psv" => '|',
            _ => ','
        };
        var dialect = new DialectDescriptor(Header: false, HeaderRows: [], Delimiter: defaultDelimiter);
        if (TryCharacter(format, "delimiter", out var delimiter) && delimiter.HasValue)
            dialect = dialect with { Delimiter = delimiter.Value };
        if (TryString(format, "lineTerminator", out var terminator) && terminator is not null)
            dialect = dialect with { LineTerminator = terminator };
        if (TryCharacter(format, "quoteChar", out var quote)) dialect = dialect with { QuoteChar = quote };
        if (TryCharacter(format, "escapeChar", out var escape)) dialect = dialect with { EscapeChar = escape };
        if (TryString(format, "nullSequence", out var nullSequence)) dialect = dialect with { NullSequence = nullSequence };
        if (TryBoolean(format, "doubleQuote", out var doubleQuote)) dialect = dialect with { DoubleQuote = doubleQuote };
        if (TryBoolean(format, "skipInitialSpace", out var skip)) dialect = dialect with { SkipInitialSpace = skip };

        var header = settings.Header.ToLowerInvariant() switch
        {
            "present" => CsvHeaderMode.Present,
            "absent" => CsvHeaderMode.Absent,
            _ when TryBoolean(format, "header", out var configured) =>
                configured ? CsvHeaderMode.Present : CsvHeaderMode.Absent,
            _ => CsvHeaderMode.Auto
        };

        return new CsvInferenceOptions
        {
            MaximumRecords = settings.MaximumRecords,
            MaximumBytes = settings.MaximumBytes,
            Header = header,
            InferDialect = !settings.NoDialectInference,
            Culture = string.IsNullOrWhiteSpace(settings.Culture)
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(settings.Culture),
            Dialect = dialect,
            Resource = new ResourceDescriptor(format?.Encoding, format?.Compression)
        };
    }

    private static bool TryCharacter(DataFormat? format, string name, out char? value)
    {
        value = null;
        if (format?.Options.TryGetValue(name, out var raw) != true) return false;
        if (raw is null) return true;
        if (raw is char character) { value = character; return true; }
        if (raw is string { Length: 1 } text) { value = text[0]; return true; }
        return false;
    }

    private static bool TryString(DataFormat? format, string name, out string? value)
    {
        value = null;
        if (format?.Options.TryGetValue(name, out var raw) != true) return false;
        value = raw?.ToString();
        return true;
    }

    private static bool TryBoolean(DataFormat? format, string name, out bool value)
    {
        value = false;
        if (format?.Options.TryGetValue(name, out var raw) != true) return false;
        if (raw is bool boolean) { value = boolean; return true; }
        return raw is string text && bool.TryParse(text, out value);
    }
}
