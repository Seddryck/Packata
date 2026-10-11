using System.Text;
using Packata.Core.Contracts;
using PocketCsvReader;

namespace Packata.ResourceReaders;

/// <summary>Infers a standards-neutral canonical contract from a bounded CSV sample.</summary>
public sealed class CsvDataContractInference
{
    private readonly CsvDataContractInferenceOptions _options;

    public CsvDataContractInference(CsvDataContractInferenceOptions? options = null)
    {
        _options = options ?? new CsvDataContractInferenceOptions();
        ArgumentNullException.ThrowIfNull(_options.InferenceOptions);
    }

    /// <summary>Infers a contract from a file and binds the inferred asset to that path.</summary>
    public DataContractInferenceResult Infer(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var inferred = new CsvInference(_options.InferenceOptions).Infer(sourcePath);
        return Map(inferred, sourcePath);
    }

    /// <summary>
    /// Infers a contract from the stream's current position and binds the inferred asset to
    /// <paramref name="sourcePath"/>. Stream behavior is controlled by PocketCsvReader's
    /// <see cref="CsvInferenceOptions.RestoreStreamPosition"/> option.
    /// </summary>
    public DataContractInferenceResult Infer(Stream stream, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var inferred = new CsvInference(_options.InferenceOptions).Infer(stream);
        return Map(inferred, sourcePath);
    }

    private DataContractInferenceResult Map(CsvInferenceResult inferred, string sourcePath)
    {
        var sourceName = GetSourceName(sourcePath);
        var assetId = _options.AssetId ?? ToIdentifier(sourceName);
        var assetName = _options.AssetName ?? sourceName;
        var endpointId = _options.EndpointId ?? $"{assetId}-source";
        var identity = _options.ContractIdentity ?? new ContractIdentity($"{assetId}-contract", assetName);
        var metadata = _options.ContractMetadata ?? new ContractMetadata($"{assetName} data contract");

        var fields = inferred.Columns.Select(MapColumn).ToArray();
        var format = new DataFormat(
            "csv",
            "text/csv",
            _options.InferenceOptions.Resource?.Encoding,
            GetCompression(sourcePath),
            MapDialect(inferred.Dialect));
        var endpoint = new DataEndpoint(
            endpointId,
            _options.EndpointName ?? $"{assetName} source",
            EndpointKind.File,
            null,
            new PathLocation([sourcePath]),
            format);
        var asset = new DataAsset(
            assetId,
            assetName,
            _options.AssetPhysicalName ?? Path.GetFileName(sourcePath),
            _options.AssetDescription,
            AssetKind.File,
            new DataSchema(fields),
            [new EndpointBinding(endpoint.Id)]);
        var contract = new DataContract(identity, metadata, [asset], [endpoint], new ContractGovernance());
        var diagnostics = inferred.Diagnostics.Select(diagnostic => new ContractInferenceDiagnostic(
            diagnostic.Code,
            diagnostic.Severity == InferenceDiagnosticSeverity.Warning
                ? InferenceSeverity.Warning
                : InferenceSeverity.Information,
            sourcePath,
            diagnostic.Message,
            diagnostic.Confidence)).ToArray();
        return new DataContractInferenceResult(contract, diagnostics);
    }

    private static DataField MapColumn(CsvInferredColumn column)
    {
        var (logicalType, format) = MapType(column.RuntimeType);
        return new DataField(column.Name, logicalType, Format: format, Required: !column.IsNullable);
    }

    private static (string LogicalType, string? Format) MapType(Type type)
    {
        if (type == typeof(bool)) return ("boolean", null);
        if (type == typeof(short)) return ("integer", "i16");
        if (type == typeof(int)) return ("integer", "i32");
        if (type == typeof(long)) return ("integer", "i64");
        if (type == typeof(float)) return ("number", "fp32");
        if (type == typeof(double)) return ("number", "fp64");
        if (type == typeof(decimal)) return ("number", null);
        if (type == typeof(DateOnly)) return ("date", null);
        if (type == typeof(TimeOnly)) return ("time", null);
        if (type == typeof(DateTimeOffset)) return ("timestamp", null);
        if (type == typeof(DateTime)) return ("datetime", null);
        if (type == typeof(Guid)) return ("string", "uuid");
        return ("string", null);
    }

    private static IReadOnlyDictionary<string, object?> MapDialect(DialectDescriptor dialect) =>
        new Dictionary<string, object?>
        {
            ["delimiter"] = dialect.Delimiter,
            ["header"] = dialect.Header,
            ["headerRows"] = dialect.HeaderRows,
            ["headerJoin"] = dialect.HeaderJoin,
            ["headerRepeat"] = dialect.HeaderRepeat,
            ["commentRows"] = dialect.CommentRows,
            ["commentChar"] = dialect.CommentChar,
            ["lineTerminator"] = dialect.LineTerminator,
            ["quoteChar"] = dialect.QuoteChar,
            ["doubleQuote"] = dialect.DoubleQuote,
            ["escapeChar"] = dialect.EscapeChar,
            ["nullSequence"] = dialect.NullSequence,
            ["missingCell"] = dialect.MissingCell,
            ["skipInitialSpace"] = dialect.SkipInitialSpace,
            ["arrayDelimiter"] = dialect.ArrayDelimiter,
            ["arrayPrefix"] = dialect.ArrayPrefix,
            ["arraySuffix"] = dialect.ArraySuffix
        };

    private string? GetCompression(string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(_options.InferenceOptions.Resource?.Compression))
            return _options.InferenceOptions.Resource.Compression;
        return Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant() switch
        {
            "gz" or "gzip" or "zip" or "deflate" or "def" or "zz" =>
                Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
            _ => null
        };
    }

    private static string GetSourceName(string sourcePath)
    {
        var filename = Path.GetFileName(sourcePath);
        var name = Path.GetFileNameWithoutExtension(filename);
        if (Path.GetExtension(filename).TrimStart('.').ToLowerInvariant() is
            "gz" or "gzip" or "zip" or "deflate" or "def" or "zz")
            name = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(name) ? "data" : name;
    }

    private static string ToIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        var separator = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                separator = false;
            }
            else if (builder.Length > 0 && !separator)
            {
                builder.Append('-');
                separator = true;
            }
        }
        return builder.ToString().TrimEnd('-') is { Length: > 0 } identifier ? identifier : "data";
    }
}
