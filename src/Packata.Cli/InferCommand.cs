using System.Globalization;
using DubUrl.Discovery;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
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
        var databaseOperation = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = ContractDocument.Load(settings.Source);
            CliDatabase.ResolveOverrides(document.Contract.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            var asset = SelectAsset(document.Contract, settings.Asset);
            var endpoint = SelectEndpoint(document.Contract, asset);
            databaseOperation = endpoint.Kind == EndpointKind.Database;
            var inference = endpoint.Kind switch
            {
                EndpointKind.File => await InferCsvAsync(document, asset, endpoint, settings).ConfigureAwait(false),
                EndpointKind.Database => await InferDatabaseAsync(
                    document, asset, endpoint, settings, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException(
                    $"Asset '{asset.Id}' is not bound to a supported file or database endpoint.")
            };

            InferencePresenter.Write(document.MappingDiagnostics, inference.Result,
                inference.Asset, inference.Endpoint);

            if (settings.Write || settings.Output is not null)
            {
                var update = ContractDocumentUpdater.Update(
                    document, asset, endpoint, inference.Asset, inference.Endpoint);
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
            if (databaseOperation) return CliOutput.Error(exception);
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            return 1;
        }
    }

    private static async Task<InferenceSelection> InferCsvAsync(
        ContractDocument document, DataAsset asset, DataEndpoint endpoint, InferSettings settings)
    {
        ValidateCsvEndpoint(asset, endpoint);
        await using var stream = await document.OpenAssetStreamAsync(asset, endpoint).ConfigureAwait(false);
        var options = new CsvDataContractInferenceOptions
        {
            InferenceOptions = CreateOptions(settings, endpoint),
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
        return new(result, result.Contract.Assets.Single(), result.Contract.Endpoints.Single());
    }

    private static async Task<InferenceSelection> InferDatabaseAsync(
        ContractDocument document, DataAsset asset, DataEndpoint endpoint, InferSettings settings, CancellationToken cancellationToken)
    {
        if (endpoint.Location is not ConnectionLocation location)
            throw new NotSupportedException(
                $"Asset '{asset.Id}' must bind a database connection for database inference.");

        var physicalName = PhysicalName(asset, endpoint, location);
        var options = new DatabaseDataContractInferenceOptions
        {
            DiscoveryOptions = new DiscoveryOptions { AllowPartial = true, IncludeReferencedTables = true },
            ContractIdentity = document.Contract.Identity,
            ContractMetadata = document.Contract.Metadata,
            Assets = new Dictionary<string, DatabaseAssetInferenceOptions>(StringComparer.Ordinal)
            {
                [physicalName] = new(asset.Id, asset.Name, asset.Description)
            },
            EndpointId = endpoint.Id,
            EndpointName = endpoint.Name,
            Environment = endpoint.Environment
        };
        var rootPath = Path.GetDirectoryName(document.SourcePath);
        DataContractInferenceResult result;
        using var database = new CliDatabase(new CliConfiguration(), rootPath ?? "", document.Contract.Endpoints,
            settings.Connectors, settings.ConnectionEnvironment);
        try
        {
            var connection = await database.ResolveAsync(endpoint, cancellationToken).ConfigureAwait(false);
            result = await new DatabaseDataContractInference(options).InferAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Packata.Database.DatabaseConnectionException error)
        {
            throw new CliException(error.Code + ": " + error.Message +
                " Use 'packata connector list', '--connector SCHEME=ID', or 'packata config list' to check acquisition settings. Downloads require a distribution catalog with archive URLs and hashes; configure one with 'packata config set connectors.catalog <path>'.");
        }
        catch (CliException) { throw; }
        catch { throw new CliException("Database inference failed. Check connectivity, permissions, connector compatibility and runtime credentials."); }
        var contract = result.RequireContract();
        var inferredAsset = SelectInferredDatabaseAsset(contract, asset, physicalName);
        var inferredEndpoint = contract.Endpoints.Single(item =>
            item.Id == inferredAsset.EndpointBindings.Single().EndpointId);
        if (database.HasOverride(endpoint.Id))
            inferredEndpoint = inferredEndpoint with { Location = new ConnectionLocation(location.Scheme, Namespace: location.Namespace) };
        return new(result, inferredAsset, inferredEndpoint);
    }

    private static string PhysicalName(DataAsset asset, DataEndpoint endpoint, ConnectionLocation location)
    {
        var binding = asset.EndpointBindings.Single();
        var name = binding.AssetPath ?? asset.PhysicalName ?? asset.Name;
        if (name.Contains('.')) return name;
        var qualifiers = new[]
        {
            OptionString(endpoint.Format, "catalog"),
            OptionString(endpoint.Format, "databaseQualifier"),
            location.Namespace,
            name
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join('.', qualifiers);
    }

    private static DataAsset SelectInferredDatabaseAsset(
        DataContract contract, DataAsset original, string physicalName)
    {
        var exact = contract.Assets.Where(candidate =>
            string.Equals(candidate.PhysicalName, physicalName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Id, original.Id, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1) return exact[0];

        var unqualified = physicalName.Split('.').Last();
        var byName = contract.Assets.Where(candidate =>
            string.Equals(candidate.Name, unqualified, StringComparison.OrdinalIgnoreCase)).ToArray();
        return byName.Length == 1
            ? byName[0]
            : throw new InvalidOperationException(
                $"Database object '{physicalName}' for asset '{original.Id}' could not be resolved uniquely.");
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

    private static void ValidateCsvEndpoint(DataAsset asset, DataEndpoint endpoint)
    {
        if (endpoint.Kind != EndpointKind.File || endpoint.Location is not PathLocation { Paths.Count: 1 })
            throw new NotSupportedException(
                $"Asset '{asset.Id}' must bind exactly one file endpoint for CSV inference.");

        var name = endpoint.Format?.Name?.Trim().TrimStart('.').ToLowerInvariant();
        var pathExtension = Path.GetExtension(((PathLocation)endpoint.Location).Paths[0])
            .TrimStart('.').ToLowerInvariant();
        var effectiveFormat = name ?? pathExtension;
        if (!SupportedFormats.Contains(effectiveFormat, StringComparer.Ordinal))
            throw new NotSupportedException($"Asset '{asset.Id}' is not bound to a supported CSV resource.");
    }

    private static CsvInferenceOptions CreateOptions(InferSettings settings, DataEndpoint endpoint)
    {
        var format = endpoint.Format;
        return new CsvInferenceOptions
        {
            MaximumRecords = settings.MaximumRecords,
            MaximumBytes = settings.MaximumBytes,
            Header = ResolveHeader(settings.Header, format),
            InferDialect = !settings.NoDialectInference,
            Culture = string.IsNullOrWhiteSpace(settings.Culture)
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(settings.Culture),
            Dialect = CreateDialect(format),
            Resource = new ResourceDescriptor(format?.Encoding, format?.Compression)
        };
    }

    private static DialectDescriptor CreateDialect(DataFormat? format)
    {
        var defaultDelimiter = format?.Name?.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "tsv" => '\t',
            "psv" => '|',
            _ => ','
        };
        var defaults = new DialectDescriptor(Header: false, HeaderRows: [], Delimiter: defaultDelimiter);
        return defaults with
        {
            Delimiter = CharacterOrDefault(format, "delimiter", defaults.Delimiter) ?? defaults.Delimiter,
            LineTerminator = StringOrDefault(format, "lineTerminator", defaults.LineTerminator)
                ?? defaults.LineTerminator,
            QuoteChar = CharacterOrDefault(format, "quoteChar", defaults.QuoteChar),
            EscapeChar = CharacterOrDefault(format, "escapeChar", defaults.EscapeChar),
            NullSequence = StringOrDefault(format, "nullSequence", defaults.NullSequence),
            DoubleQuote = BooleanOrDefault(format, "doubleQuote", defaults.DoubleQuote),
            SkipInitialSpace = BooleanOrDefault(format, "skipInitialSpace", defaults.SkipInitialSpace)
        };
    }

    private static CsvHeaderMode ResolveHeader(string header, DataFormat? format) =>
        header.ToLowerInvariant() switch
        {
            "present" => CsvHeaderMode.Present,
            "absent" => CsvHeaderMode.Absent,
            _ when TryBoolean(format, "header", out var configured) =>
                configured ? CsvHeaderMode.Present : CsvHeaderMode.Absent,
            _ => CsvHeaderMode.Auto
        };

    private static char? CharacterOrDefault(DataFormat? format, string name, char? defaultValue) =>
        TryCharacter(format, name, out var value) ? value : defaultValue;

    private static string? StringOrDefault(DataFormat? format, string name, string? defaultValue) =>
        TryString(format, name, out var value) ? value : defaultValue;

    private static bool BooleanOrDefault(DataFormat? format, string name, bool defaultValue) =>
        TryBoolean(format, name, out var value) ? value : defaultValue;

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

    private static string? OptionString(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true ? value?.ToString() : null;

    private static readonly string[] SupportedFormats = ["csv", "tsv", "psv"];

    private sealed record InferenceSelection(
        DataContractInferenceResult Result,
        DataAsset Asset,
        DataEndpoint Endpoint);
}
