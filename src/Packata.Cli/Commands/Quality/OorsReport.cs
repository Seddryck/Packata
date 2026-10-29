using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Packata.Cli;

/// <summary>OORS is the only wire model. Packata execution details use vendor-attributed extensions.</summary>
internal static class OorsReport
{
    internal const string ApiVersion = "v0.8.0";
    internal const int MaximumOutputCharacters = 4096;
    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(OorsReport).Assembly.GetManifestResourceStream("Packata.Cli.OorsSchema")!;
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    internal static JObject Create(QualityRunReport report, DataContract contract, string specification,
        ContractSource source, QualityFilters filters)
    {
        var document = new JObject
        {
            ["apiVersion"] = ApiVersion, ["kind"] = "ObservabilityResults", ["id"] = Guid.NewGuid().ToString(),
            ["observedAt"] = Timestamp(report.FinishedAt),
            ["source"] = new JObject { ["process"] = "packata quality run", ["runId"] = report.Id, ["vendor"] = "packata" },
            ["run"] = new JObject { ["state"] = "completed" },
            ["contract"] = Object(("contractId", contract.Identity.Id), ("standard", "ODCS"),
                ("version", contract.Identity.Version), ("server", Server(report, contract))),
            ["customProperties"] = DocumentProperties(report, contract, specification, source, filters),
            ["results"] = Results(report.Results)
        };
        return (JObject)CommandOutput.RedactedJson(document);
    }

    private static JArray DocumentProperties(QualityRunReport report, DataContract contract, string specification,
        ContractSource source, QualityFilters filters)
    {
        var properties = new JArray();
        Add(properties, "runTiming", Timing(report.StartedAt, report.FinishedAt, report.Duration));
        Add(properties, "contractProvenance", Object(("name", contract.Identity.Name), ("specification", specification.TrimStart('v')),
            ("source", SafeSource(source.Source)), ("fingerprint", Fingerprint(source.OriginalText))));
        Add(properties, "runner", new JObject { ["name"] = "packata", ["version"] = ApplicationVersion.Value });
        Add(properties, "engines", new JArray(report.Engines.Select(Engine)));
        var applied = Object(("id", Array(filters.Id)), ("asset", Array(filters.Asset)), ("field", Array(filters.Field)),
            ("type", Array(filters.Type)), ("dimension", Array(filters.Dimension)));
        if (applied.Count > 0) Add(properties, "filters", applied);
        Add(properties, "summary", Summary(report));
        return properties;
    }

    private static JObject Engine(QualityEngineInfo engine) => Object(("id", engine.Id), ("version", engine.Version),
        ("types", new JArray(engine.Types)), ("runtime", engine.Runtime is null ? null
            : new JObject { ["name"] = engine.Runtime.Name, ["version"] = engine.Runtime.Version }));

    private static JObject Summary(QualityRunReport report)
    {
        var summary = new JObject { ["outcome"] = Status(report.Outcome) };
        foreach (var status in new[] { "pass", "fail", "warn", "skip", "error" })
            summary[status] = report.Results.Count(r => Status(r.Outcome) == status);
        return summary;
    }

    private static JArray Results(IReadOnlyList<QualityRunResult> entries)
    {
        var duplicateIds = entries.Where(r => r.Rule.SourceId is not null).GroupBy(r => r.Rule.SourceId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key!).ToHashSet(StringComparer.Ordinal);
        // Reserve native IDs before deriving IDs, including collisions between a native ID and a contract path.
        var used = entries.Where(r => r.Rule.SourceId is not null && !duplicateIds.Contains(r.Rule.SourceId))
            .Select(r => r.Rule.SourceId!).ToHashSet(StringComparer.Ordinal);
        var results = new JArray();
        for (var index = 0; index < entries.Count; index++)
        {
            var result = entries[index];
            var rule = result.Rule;
            var id = rule.SourceId;
            if (id is null || duplicateIds.Contains(id))
            {
                var basis = rule.SourcePath ?? $"rule/{index}";
                id = basis;
                for (var suffix = 1; !used.Add(id); suffix++) id = $"{basis}#{suffix}";
            }
            results.Add(Result(result, id));
        }
        return results;
    }

    private static string? Server(QualityRunReport report, DataContract contract)
    {
        var servers = report.Results.Select(r => r.Execution?.Descriptor.EndpointId).OfType<string>().Distinct().ToArray();
        // A single declared server also applies to descriptive rules with no execution descriptor.
        return servers.Length == 1 ? servers[0] : contract.Endpoints.Count == 1 ? contract.Endpoints[0].Id : null;
    }

    private static JObject Result(QualityRunResult result, string id)
    {
        var rule = result.Rule;
        var field = rule.Target?.FieldPath.Count > 0 ? string.Join('.', rule.Target.FieldPath) : null;
        var properties = ResultProperties(result, field);
        var name = rule.Metadata.Name ?? rule.Metadata.Description ?? rule.Description ?? $"Quality rule {id}";
        var output = Object(("id", id), ("type", "check"), ("name", name), ("status", Status(result.Outcome)),
            ("relatesTo", rule.SourcePath), ("dimension", rule.Metadata.Dimension), ("severity", rule.Metadata.Severity),
            ("message", result.Message), ("tags", Array(rule.Metadata.Tags)));
        if (rule.Target is { } target)
            output["target"] = new JObject { ["resourceType"] = field is null ? "table" : "column",
                ["resourceIdentifier"] = field is null ? target.AssetId : target.AssetId + "." + field };
        var measure = Measurement(result, properties);
        if (measure.Count > 0) output["measure"] = measure;
        output["customProperties"] = properties;
        return output;
    }

    private static JArray ResultProperties(QualityRunResult result, string? field)
    {
        var rule = result.Rule;
        var properties = new JArray();
        Add(properties, "ruleId", rule.SourceId);
        Add(properties, "asset", rule.Target?.AssetId);
        Add(properties, "field", field);
        Add(properties, "ruleType", rule.Kind);
        Add(properties, "description", rule.Metadata.Description ?? rule.Description);
        if (result.Execution is { Outcome: not RuleOutcome.Skipped } execution)
            Add(properties, "duration", Duration(execution.Duration));
        return properties;
    }

    private static JObject Measurement(QualityRunResult result, JArray properties)
    {
        var rule = result.Rule;
        var measure = new JObject();
        if (rule.Definition is LibraryQuality library) measure["metric"] = library.Metric;
        if (result.Execution?.Measurement.Unit is { } unit) measure["unit"] = unit;
        var threshold = Threshold(rule.Expectations);
        if (threshold.Count > 0) measure["threshold"] = threshold;
        if (result.Execution?.Measurement.Value is { } value)
        {
            var observed = BoundedValue(value.ToObject());
            if (observed.Type is JTokenType.Integer or JTokenType.Float or JTokenType.String) measure["value"] = observed;
            else Add(properties, "output", observed);
        }
        return measure;
    }

    private static JObject Threshold(IReadOnlyList<QualityExpectation> expectations)
    {
        var threshold = new JObject();
        foreach (var expectation in expectations)
        {
            var key = OperatorName(expectation.Operator);
            threshold[key] = expectation.Upper is null ? BoundedValue(expectation.Operand.ToObject())
                : new JArray(BoundedValue(expectation.Operand.ToObject()), BoundedValue(expectation.Upper.ToObject()));
        }
        return threshold;
    }

    private static string OperatorName(ExpectationOperator op) => op switch
    {
        ExpectationOperator.Equal => "mustBe", ExpectationOperator.NotEqual => "mustNotBe",
        ExpectationOperator.Greater => "mustBeGreaterThan", ExpectationOperator.GreaterOrEqual => "mustBeGreaterOrEqualTo",
        ExpectationOperator.Less => "mustBeLessThan", ExpectationOperator.LessOrEqual => "mustBeLessOrEqualTo",
        ExpectationOperator.Between => "mustBeBetween", ExpectationOperator.NotBetween => "mustNotBeBetween",
        _ => throw new InvalidOperationException("Unsupported expectation operator.")
    };

    private static JToken? Array(IReadOnlyList<string> values) => values.Count > 0 ? new JArray(values) : null;
    private static JObject Object(params (string Key, object? Value)[] values)
    {
        var obj = new JObject();
        foreach (var (key, value) in values)
            if (value is not null) obj[key] = value is JToken token ? token : JToken.FromObject(value);
        return obj;
    }
    private static void Add(JArray properties, string name, object? value)
    {
        if (value is null) return;
        properties.Add(new JObject { ["property"] = name, ["vendor"] = "packata",
            ["value"] = value is JToken token ? token : JToken.FromObject(value) });
    }
    internal static string Status(RuleOutcome outcome) => outcome switch
    {
        RuleOutcome.Passed => "pass", RuleOutcome.Failed => "fail", RuleOutcome.Warn => "warn",
        RuleOutcome.Skipped => "skip", _ => "error"
    };
    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string Duration(TimeSpan value) => value.TotalSeconds.ToString("0.#########", CultureInfo.InvariantCulture) + "s";
    private static JObject Timing(DateTimeOffset start, DateTimeOffset end, TimeSpan elapsed) => new()
        { ["startedAt"] = Timestamp(start), ["finishedAt"] = Timestamp(end), ["duration"] = Duration(elapsed) };
    internal static void Stamp(JObject document, DateTimeOffset start, DateTimeOffset end, TimeSpan elapsed)
    {
        document["observedAt"] = Timestamp(end);
        document["customProperties"]!.Single(p => (string?)p["property"] == "runTiming")["value"] = Timing(start, end, elapsed);
    }

    internal static string SafeSource(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            var safe = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            return CommandOutput.Redact(safe.Uri.AbsoluteUri);
        }
        return CommandOutput.Redact(source);
    }

    internal static string Fingerprint(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        JToken Canonical(YamlNode node) => node switch
        {
            YamlMappingNode map => new JObject(map.Children.OrderBy(p => ((YamlScalarNode)p.Key).Value, StringComparer.Ordinal)
                .Select(p => new JProperty(((YamlScalarNode)p.Key).Value!, Canonical(p.Value)))),
            YamlSequenceNode sequence => new JArray(sequence.Children.Select(Canonical)),
            YamlScalarNode scalar => Object(("tag", scalar.Tag.ToString()), ("style", scalar.Style.ToString()), ("value", scalar.Value)),
            _ => throw new InvalidOperationException("Unsupported contract node.")
        };
        var canonical = Canonical(stream.Documents.Single().RootNode).ToString(Formatting.None);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static JToken BoundedValue(object? value) => CommandOutput.RedactedJson(new OutputBudget().Limit(value, 0));

    private sealed class OutputBudget
    {
        private int _remaining = MaximumOutputCharacters;

        internal JToken Limit(object? item, int depth)
        {
            if (_remaining <= 0 || depth > 6) return new JValue("[truncated]");
            _remaining -= 8;
            if (item is IReadOnlyDictionary<string, object?> map)
                return Map(map, depth);
            if (item is System.Collections.IEnumerable sequence && item is not string)
                return Sequence(sequence, depth);
            if (item is string text) return Text(text);
            return item is null ? JValue.CreateNull() : JToken.FromObject(item);
        }

        private JObject Map(IReadOnlyDictionary<string, object?> map, int depth)
        {
            var obj = new JObject();
            foreach (var (key, child) in map.Take(32))
            {
                if (_remaining <= 0) { obj["truncated"] = true; break; }
                var name = key.Length > 128 ? key[..128] : key;
                _remaining -= name.Length;
                obj[name] = Limit(child, depth + 1);
            }
            if (map.Count > 32) obj["truncated"] = true;
            return obj;
        }

        private JArray Sequence(System.Collections.IEnumerable sequence, int depth)
        {
            var array = new JArray();
            var count = 0;
            foreach (var child in sequence)
            {
                if (_remaining <= 0 || count++ >= 32) { array.Add("[truncated]"); break; }
                array.Add(Limit(child, depth + 1));
            }
            return array;
        }

        private JValue Text(string text)
        {
            var length = Math.Min(Math.Max(0, _remaining), text.Length);
            _remaining -= length;
            return new JValue(length == text.Length ? text : text[..length] + "[truncated]");
        }
    }

    internal static void Validate(JObject document)
    {
        using var json = JsonDocument.Parse(document.ToString(Formatting.None));
        if ((string?)document["apiVersion"] != ApiVersion || !Schema.Value.Evaluate(json.RootElement,
            new EvaluationOptions { RequireFormatValidation = true }).IsValid)
            throw new InvalidOperationException("The generated report does not conform to OORS v0.8.0.");
    }
    internal static string Serialize(JObject document, string format)
    {
        Validate(document);
        if (format == "json") return document.ToString(Formatting.Indented);
        if (format != "yaml") throw new ArgumentException("Unsupported OORS serialization.");
        object? Plain(JToken token) => token switch
        {
            JObject obj => obj.Properties().ToDictionary(p => p.Name, p => Plain(p.Value)),
            JArray array => array.Select(Plain).ToArray(),
            JValue scalar => scalar.Value,
            _ => null
        };
        return new SerializerBuilder().WithQuotingNecessaryStrings().Build().Serialize(Plain(document));
    }
}
