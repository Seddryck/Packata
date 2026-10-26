using System.Globalization;
using DubUrl.Discovery;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.ResourceReaders.Parquet;
using Packata.ResourceReaders.Excel;
using PocketCsvReader;

namespace Packata.Cli;

internal static class InferenceService
{
    internal static async Task<InferenceSelection> InferFileAsync(ContractDocument document, DataAsset asset,
        DataEndpoint endpoint, InferSettings settings, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (endpoint.Location is not PathLocation { Paths.Count: 1 } paths)
            throw new NotSupportedException("File inference requires exactly one bound path.");
        var format = new DataFormatResolver().Resolve(endpoint);
        if (format.Name is "xls" or "xlsx")
        {
            RejectOptions(arguments, ["--culture", "--no-dialect-inference"]);
            RejectExternalCompression(format);
            return await InferExcelAsync(document, asset, endpoint, settings, paths.Paths[0], format.Name, cancellationToken).ConfigureAwait(false);
        }
        RejectOptions(arguments, ["--sheet-name", "--sheet-number"]);
        if (format.Name is not ("parquet" or "pqt"))
            return await InferCsvAsync(document, asset, endpoint, settings).ConfigureAwait(false);
        RejectOptions(arguments, ["--maximum-records", "--maximum-bytes", "--culture", "--header", "--no-dialect-inference"]);
        RejectExternalCompression(format);
        await using var stream = await document.OpenAssetStreamAsync(asset, endpoint).ConfigureAwait(false);
        var options = new ParquetDataContractInferenceOptions
        {
            ContractIdentity = document.Contract.Identity, ContractMetadata = document.Contract.Metadata,
            AssetId = asset.Id, AssetName = asset.Name, AssetPhysicalName = asset.PhysicalName,
            AssetDescription = asset.Description, EndpointId = endpoint.Id, EndpointName = endpoint.Name
        };
        var result = await new ParquetDataContractInference(options).InferAsync(stream, paths.Paths[0], cancellationToken)
            .ConfigureAwait(false);
        return new(result, result.Contract.Assets.Single(), result.Contract.Endpoints.Single());
    }

    private static void RejectExternalCompression(ResolvedDataFormat format)
    {
        if (format.Compression is not null)
            throw new NotSupportedException("Externally compressed binary files are unsupported for inference.");
    }

    private static async Task<InferenceSelection> InferExcelAsync(ContractDocument document, DataAsset asset,
        DataEndpoint endpoint, InferSettings settings, string path, string formatName, CancellationToken cancellationToken)
    {
        await using var stream = await document.OpenAssetStreamAsync(asset, endpoint).ConfigureAwait(false);
        var options = new ExcelDataContractInferenceOptions
        {
            ContractIdentity = document.Contract.Identity, ContractMetadata = document.Contract.Metadata,
            AssetId = asset.Id, AssetName = asset.Name, AssetPhysicalName = asset.PhysicalName,
            AssetDescription = asset.Description, EndpointId = endpoint.Id, EndpointName = endpoint.Name,
            SheetName = settings.SheetName, SheetNumber = settings.SheetNumber,
            MaximumRecords = settings.MaximumRecords, MaximumBytes = settings.MaximumBytes ?? 64 * 1024 * 1024,
            SourceFormat = (endpoint.Format ?? new DataFormat(formatName)) with { Name = formatName },
            Header = settings.Header.ToLowerInvariant() switch
            {
                "present" => ExcelHeaderMode.Present, "absent" => ExcelHeaderMode.Absent, _ => ExcelHeaderMode.Auto
            }
        };
        var result = new ExcelDataContractInference(options).Infer(stream, path, cancellationToken);
        var inferred = result.Contract.Assets.Single();
        // Sampling cannot weaken or strengthen an authored requiredness constraint.
        var originalFields = asset.Schema?.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var schema = inferred.Schema ?? throw new InvalidOperationException("Excel inference did not return a schema.");
        inferred = inferred with { Schema = schema with
        {
            Fields = schema.Fields.Select(field => PreserveAuthoredField(field, originalFields)).ToArray()
        } };
        return new(result, inferred, result.Contract.Endpoints.Single());
    }

    private static DataField PreserveAuthoredField(DataField field, IReadOnlyDictionary<string, DataField>? originals)
    {
        if (originals is null || !originals.TryGetValue(field.Name, out var original)) return field;
        return field with
        {
            Required = original.Required, Nullable = original.Nullable,
            LogicalType = field.LogicalType ?? original.LogicalType,
            Format = field.LogicalType is null ? original.Format : field.Format
        };
    }

    internal static void RejectOptions(IReadOnlyList<string> arguments, IReadOnlyList<string> unsupported)
    {
        var invalid = unsupported.Where(option => arguments.Any(argument => argument == option
            || argument.StartsWith(option + "=", StringComparison.Ordinal))).ToArray();
        if (invalid.Length > 0)
            throw new NotSupportedException($"Options {string.Join(", ", invalid)} do not apply to this resource format.");
    }

    internal static async Task<InferenceSelection> InferCsvAsync(
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

    internal static async Task<InferenceSelection> InferDatabaseAsync(
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

    internal static DataAsset SelectAsset(DataContract contract, string assetId)
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

    internal static DataEndpoint SelectEndpoint(DataContract contract, DataAsset asset)
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

    internal sealed record InferenceSelection(
        DataContractInferenceResult Result,
        DataAsset Asset,
        DataEndpoint Endpoint);
}
