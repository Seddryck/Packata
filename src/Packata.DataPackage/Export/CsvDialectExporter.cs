using System.Collections;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Packata.Core.Contracts;

namespace Packata.DataPackage.Export;

/// <summary>Exports the CSV portion of a Data Package v2 Table Dialect from a selected endpoint.</summary>
public sealed class CsvDialectExporter
{
    public const string Profile = "https://datapackage.org/profiles/2.0/tabledialect.json";

    private static readonly string[] Properties =
    [
        "header", "headerRows", "headerJoin", "commentRows", "commentChar", "delimiter",
        "lineTerminator", "quoteChar", "doubleQuote", "escapeChar", "nullSequence", "skipInitialSpace"
    ];

    /// <summary>Returns standalone JSON and diagnostics. Does not read endpoint data.</summary>
    public MappingResult<string> Export(DataContract contract, string endpointId)
    {
        var result = CreateDescriptor(contract, endpointId);
        return new(result.IsSuccessful ? Serialize(result.Value!) : null, result.Diagnostics);
    }

    /// <summary>Maps an exact, unique endpoint identifier to the existing Data Package dialect model.</summary>
    public MappingResult<TableDelimitedDialect> CreateDescriptor(DataContract contract, string endpointId)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var diagnostics = new List<MappingDiagnostic>();
        MappingResult<TableDelimitedDialect> Fail(string code, string path, string message)
            => MappingResult<TableDelimitedDialect>.Failure(new MappingDiagnostic(code, MappingSeverity.Error, path, message));

        if (string.IsNullOrWhiteSpace(endpointId))
            return Fail("CSV001", "$.endpoints", "Select a CSV endpoint by its exact identifier.");
        var matches = contract.Endpoints.Where(endpoint => endpoint.Id == endpointId).ToArray();
        if (matches.Length != 1)
            return Fail("CSV002", "$.endpoints", matches.Length == 0
                ? $"Endpoint '{endpointId}' was not found. Inspect the contract to list endpoint identifiers."
                : $"Endpoint identifier '{endpointId}' is ambiguous. Use unique endpoint identifiers.");

        var endpoint = matches[0];
        var path = $"$.endpoints[{contract.Endpoints.ToList().IndexOf(endpoint)}].format";
        if (!IsCsv(endpoint))
            return Fail("CSV003", path, "The selected endpoint must describe a CSV file (format csv, text/csv, or a .csv path).");

        var dialect = new TableDelimitedDialect { Profile = Profile };
        var options = endpoint.Format?.Options ?? new Dictionary<string, object?>();
        foreach (var (name, value) in options)
        {
            if (Properties.Contains(name, StringComparer.Ordinal))
            {
                var property = typeof(TableDialect).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
                if (TryValue(value, property.PropertyType, out var converted)) property.SetValue(dialect, converted);
                else diagnostics.Add(new("CSV004", MappingSeverity.Error, $"{path}.options.{name}",
                    $"Invalid CSV dialect setting '{name}'; expected {property.PropertyType.Name}."));
            }
            else if (name == "dialectType" && value is null or "delimited") { }
            else if (name == "dialectProfile" && value is null or Profile or "https://datapackage.org/profiles/1.0/tabledialect.json") { }
            else if (name == "headerRepeat" && value is true) { }
            else if (value is not null)
                diagnostics.Add(new("CSV005", MappingSeverity.Warning, $"{path}.options.{name}",
                    $"Setting '{name}' is not represented by the exported CSV Table Dialect profile and was omitted."));
        }

        // Native dialect models retain [1] as their default even when header is false.
        if (!dialect.Header && (!options.ContainsKey("headerRows") || dialect.HeaderRows?.SequenceEqual([1]) == true))
            dialect.HeaderRows = [];
        void Invalid(string name, string message) => diagnostics.Add(new("CSV006", MappingSeverity.Error, $"{path}.options.{name}", message));
        if (string.IsNullOrEmpty(dialect.Delimiter)) Invalid("delimiter", "Delimiter must be a non-empty string.");
        if (string.IsNullOrEmpty(dialect.LineTerminator)) Invalid("lineTerminator", "Line terminator must be a non-empty string.");
        foreach (var (name, value) in new[] { ("quoteChar", dialect.QuoteChar), ("escapeChar", dialect.EscapeChar), ("commentChar", dialect.CommentChar) })
            if (value is not null && value.Length != 1 && !(name == "quoteChar" && value.Length == 0))
                Invalid(name, $"Setting '{name}' must contain one character (or an empty quoteChar to disable quoting).");
        if (dialect.HeaderJoin is null) Invalid("headerJoin", "Header join must be a string.");
        if (dialect.EscapeChar is not null && (!string.IsNullOrEmpty(dialect.QuoteChar) || dialect.DoubleQuote))
            Invalid("escapeChar", "Escape and quote strategies cannot be combined. Set quoteChar to null and doubleQuote to false for escape mode.");
        if (dialect.Header && dialect.HeaderRows is not { Count: > 0 }
            || !dialect.Header && dialect.HeaderRows is { Count: > 0 })
            Invalid("headerRows", "Header rows must be non-empty when header is true and empty when header is false.");
        foreach (var (name, rows) in new[] { ("headerRows", dialect.HeaderRows), ("commentRows", dialect.CommentRows) })
            if (rows?.Any(row => row <= 0) == true) Invalid(name, $"Setting '{name}' must contain positive row numbers.");

        if (dialect.QuoteChar is null)
            diagnostics.Add(new("CSV007", MappingSeverity.Warning, $"{path}.options.quoteChar",
                "The null quoteChar is exported as an empty string because the Table Dialect profile requires a string. Check the consumer's support for disabled quoting."));

        return new(diagnostics.Any(d => d.Severity == MappingSeverity.Error) ? null : dialect, diagnostics);
    }

    private static bool IsCsv(DataEndpoint endpoint)
    {
        if (endpoint.Kind is not (EndpointKind.File or EndpointKind.ObjectStorage) || endpoint.Location is not PathLocation)
            return false;
        var name = endpoint.Format?.Name?.Trim().TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(name)) return name is "csv" or "csv.gz" or "csv.gzip";
        var mediaType = endpoint.Format?.MediaType?.Split(';')[0].Trim();
        if (!string.IsNullOrEmpty(mediaType)) return mediaType.Equals("text/csv", StringComparison.OrdinalIgnoreCase);
        return endpoint.Location is PathLocation { Paths.Count: > 0 } paths && paths.Paths.All(path =>
        {
            var file = Uri.TryCreate(path, UriKind.Absolute, out var uri) ? uri.AbsolutePath : path;
            if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) file = file[..^3];
            else if (file.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase)) file = file[..^5];
            return file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TryValue(object? value, Type type, out object? converted)
    {
        converted = value;
        if (value is JValue scalar) value = scalar.Value;
        if (type == typeof(string))
        {
            converted = value is char character ? character.ToString() : value;
            return converted is null or string;
        }
        if (type == typeof(bool)) { converted = value; return value is bool; }
        if (type == typeof(List<int>))
        {
            if (value is null) { converted = null; return true; }
            if (value is not IEnumerable sequence || value is string) return false;
            var rows = new List<int>();
            foreach (var item in sequence)
            {
                var row = item is JValue token ? token.Value : item;
                if (row is int number) rows.Add(number);
                else if (row is long integer && integer is >= int.MinValue and <= int.MaxValue) rows.Add((int)integer);
                else return false;
            }
            converted = rows;
            return true;
        }
        return false;
    }

    private static string Serialize(TableDelimitedDialect dialect)
    {
        var json = JObject.FromObject(dialect, JsonSerializer.Create(new JsonSerializerSettings
        { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
        var descriptor = new JObject { ["$schema"] = dialect.Profile };
        foreach (var name in Properties)
        {
            var value = json[name]!;
            // Optional nulls are omitted. An empty quoteChar disables quoting without emitting a
            // JSON null, which the Table Dialect profile does not accept for string properties.
            if (name == "quoteChar" && value.Type == JTokenType.Null) descriptor[name] = "";
            else if (value.Type != JTokenType.Null) descriptor[name] = value.DeepClone();
        }
        return descriptor.ToString(Formatting.Indented);
    }
}
