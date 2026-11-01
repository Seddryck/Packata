using System.Globalization;
using DubUrl.Discovery;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.ResourceReaders.Parquet;
using Packata.ResourceReaders.Excel;
using PocketCsvReader;

namespace Packata.Application;

/// <summary>Coordinates format-specific inference and preserves authored identity and metadata.</summary>
public static class ContractInferenceService
{
    public static async Task<InferenceSelection> InferFileAsync(InferenceInput document, DataAsset asset,
        DataEndpoint endpoint, ContractInferenceOptions settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.Location is not PathLocation { Paths.Count: 1 } paths)
            throw new NotSupportedException("File inference requires exactly one bound path.");
        var format = new DataFormatResolver().Resolve(endpoint);
        InferenceCapabilities.Validate(format.Name, settings);
        endpoint = endpoint with { Format = (endpoint.Format ?? new DataFormat(format.Name)) with
            { Name = format.Name, Compression = format.Compression } };
        if (format.Name is "xls" or "xlsx")
        {
            RejectExternalCompression(format);
            return await InferExcelAsync(document, asset, endpoint, settings, paths.Paths[0], format.Name, cancellationToken).ConfigureAwait(false);
        }
        if (format.Name is not ("parquet" or "pqt"))
            return await InferCsvAsync(document, asset, endpoint, settings, cancellationToken).ConfigureAwait(false);
        RejectExternalCompression(format);
        await using var stream = await document.OpenAssetStreamAsync(asset, endpoint, cancellationToken,
            settings.StagingMaximumBytes).ConfigureAwait(false);
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

    private static async Task<InferenceSelection> InferExcelAsync(InferenceInput document, DataAsset asset,
        DataEndpoint endpoint, ContractInferenceOptions settings, string path, string formatName, CancellationToken cancellationToken)
    {
        await using var stream = await document.OpenAssetStreamAsync(asset, endpoint, cancellationToken,
            settings.StagingMaximumBytes, settings.MaximumBytes ?? 64 * 1024 * 1024).ConfigureAwait(false);
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

    public static async Task<InferenceSelection> InferCsvAsync(
        InferenceInput document, DataAsset asset, DataEndpoint endpoint, ContractInferenceOptions settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCsvEndpoint(asset, endpoint);
        InferenceCapabilities.Validate(new DataFormatResolver().Resolve(endpoint).Name, settings);
        await using var stream = await document.OpenAssetStreamAsync(asset, endpoint, cancellationToken).ConfigureAwait(false);
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
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, result.Contract.Assets.Single(), result.Contract.Endpoints.Single());
    }

    public static async Task<InferenceSelection> InferDatabaseAsync(
        DataContract original, DataAsset asset, DataEndpoint endpoint, DubUrl.ConnectionUrl connection,
        bool omitConnectionLocation = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.Location is not ConnectionLocation location)
            throw new NotSupportedException($"Asset '{asset.Id}' must bind a database connection for database inference.");
        var physicalName = PhysicalName(asset, endpoint, location);
        var options = new DatabaseDataContractInferenceOptions
        {
            DiscoveryOptions = new DiscoveryOptions { AllowPartial = true, IncludeReferencedTables = true },
            ContractIdentity = original.Identity, ContractMetadata = original.Metadata,
            Assets = new Dictionary<string, DatabaseAssetInferenceOptions>(StringComparer.Ordinal)
            { [physicalName] = new(asset.Id, asset.Name, asset.Description) },
            EndpointId = endpoint.Id, EndpointName = endpoint.Name, Environment = endpoint.Environment
        };
        var result = await new DatabaseDataContractInference(options).InferAsync(connection, cancellationToken).ConfigureAwait(false);
        var contract = result.RequireContract();
        var inferredAsset = SelectInferredDatabaseAsset(contract, asset, physicalName);
        var inferredEndpoint = contract.Endpoints.Single(item => item.Id == inferredAsset.EndpointBindings.Single().EndpointId);
        if (omitConnectionLocation)
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

        var unqualified = physicalName.Split('.')[^1];
        var byName = contract.Assets.Where(candidate =>
            string.Equals(candidate.Name, unqualified, StringComparison.OrdinalIgnoreCase)).ToArray();
        return byName.Length == 1
            ? byName[0]
            : throw new InvalidOperationException(
                $"Database object '{physicalName}' for asset '{original.Id}' could not be resolved uniquely.");
    }

    public static DataAsset SelectAsset(DataContract contract, string assetId)
    {
        var matches = contract.Assets.Where(asset =>
            string.Equals(asset.Id, assetId, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ContractAssetSelectionException(assetId, false),
            _ => throw new ContractAssetSelectionException(assetId, true)
        };
    }

    public static DataEndpoint SelectEndpoint(DataContract contract, DataAsset asset)
    {
        if (asset.EndpointBindings.Count != 1)
        {
            throw new InvalidOperationException(
                $"Asset '{asset.Id}' must have exactly one endpoint binding for inference.");
        }

        var endpointId = asset.EndpointBindings[0].EndpointId;
        var matches = contract.Endpoints.Where(endpoint => endpoint.Id == endpointId).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Endpoint binding '{endpointId}' for asset '{asset.Id}' could not be resolved uniquely.");
    }

    public static void ValidateCsvEndpoint(DataAsset asset, DataEndpoint endpoint)
    {
        if (endpoint.Kind != EndpointKind.File || endpoint.Location is not PathLocation { Paths.Count: 1 })
        {
            throw new NotSupportedException(
                $"Asset '{asset.Id}' must bind exactly one file endpoint for CSV inference.");
        }

        var effectiveFormat = new DataFormatResolver().Resolve(endpoint).Name;
        if (!SupportedFormats.Contains(effectiveFormat, StringComparer.Ordinal))
            throw new NotSupportedException($"Asset '{asset.Id}' is not bound to a supported CSV resource.");
    }

    public static CsvInferenceOptions CreateOptions(ContractInferenceOptions settings, DataEndpoint endpoint)
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

    public sealed record InferenceSelection(
        DataContractInferenceResult Result,
        DataAsset Asset,
        DataEndpoint Endpoint);
}
