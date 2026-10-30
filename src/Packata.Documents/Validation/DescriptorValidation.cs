using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Numerics;
using Json.Schema;
using Packata.Core.Contracts;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Packata.Documents.Validation;

[Flags]
public enum ValidationLayers { None = 0, Schema = 1, Semantic = 2 }
public enum ValidationState { Passed, Failed, Skipped }
public sealed record ValidationFinding(ValidationLayers Layer, string Code, MappingSeverity Severity, string SourcePath, string Message);
public sealed record LayerValidation(ValidationLayers Layer, ValidationState State, string? Reason = null)
{
    public bool Executed => State != ValidationState.Skipped;
}
public sealed record DescriptorValidationReport(IReadOnlyList<LayerValidation> Layers, IReadOnlyList<ValidationFinding> Diagnostics, DescriptorProfile? Profile = null)
{
    public bool Valid => Layers.Count > 0 && Layers.All(layer => layer.State == ValidationState.Passed)
        && Diagnostics.All(finding => finding.Severity != MappingSeverity.Error);
}

/// <summary>Offline validation of original YAML/JSON, before native defaults or coercion.</summary>
public static class DescriptorValidation
{
    // Deliberately explicit: adding an enum member must not change default behavior.
    public const ValidationLayers DefaultLayers = ValidationLayers.Schema | ValidationLayers.Semantic;

    public static DescriptorValidationReport Validate(string text, string format = "auto",
        ValidationLayers layers = DefaultLayers, Uri? baseUri = null)
    {
        if (format is not ("auto" or "odcs" or "datapackage")) throw new ArgumentException("Unknown input format.", nameof(format));
        if (layers == ValidationLayers.None || (layers & ~DefaultLayers) != 0) throw new ArgumentOutOfRangeException(nameof(layers));
        var findings = new List<ValidationFinding>();
        var results = new List<LayerValidation>();
        var selected = new[] { ValidationLayers.Schema, ValidationLayers.Semantic }.Where(layer => layers.HasFlag(layer)).ToArray();
        JsonObject root;
        try { root = Read(text); }
        catch (Exception exception) when (exception is YamlException or FormatException or JsonException or ArgumentException)
        {
            foreach (var layer in selected)
            {
                findings.Add(new(layer, "VAL_PARSE", MappingSeverity.Error, "$", "A single, bounded YAML/JSON mapping with unique string keys is required."));
                results.Add(new(layer, ValidationState.Skipped, "Parsing prerequisite failed."));
            }
            return new(results, findings);
        }

        return Execute(root, text, format, selected, baseUri);
    }

    private static DescriptorValidationReport Execute(JsonObject root, string text, string format, ValidationLayers[] selected, Uri? baseUri)
    {
        var findings = new List<ValidationFinding>();
        var results = new List<LayerValidation>();
        var odcs = format == "odcs" || (format == "auto" && (root.ContainsKey("kind") || root.ContainsKey("apiVersion")));
        var profile = DescriptorProfiles.Select(root, odcs);
        var selectionIssue = SelectionIssue(root, format, odcs);
        foreach (var layer in selected)
        {
            if (layer == ValidationLayers.Semantic && results.Any(result => result.Layer == ValidationLayers.Schema && result.State != ValidationState.Passed))
            {
                results.Add(new(layer, ValidationState.Skipped, "Schema validation failed; semantic validation requires a schema-valid descriptor."));
                continue;
            }
            if (selectionIssue is not null)
            {
                findings.Add(new(layer, "VAL_VERSION", MappingSeverity.Error, odcs ? "$/apiVersion" : "$",
                    selectionIssue));
                results.Add(new(layer, ValidationState.Skipped, "Standard/version selection prerequisite failed."));
                continue;
            }
            var start = findings.Count;
            EvaluateLayer(layer, root, text, format, profile ?? throw new InvalidOperationException("Profile selection failed."), baseUri, findings);
            results.Add(Outcome(layer, findings.Skip(start).ToArray()));
        }
        return new(results.AsReadOnly(), findings.Distinct().ToArray(), profile);
    }

    private static LayerValidation Outcome(ValidationLayers layer, ValidationFinding[] current)
        => new(layer, current.Any(f => f.Severity == MappingSeverity.Error) ? ValidationState.Failed : ValidationState.Passed,
            current.Any(f => f.Code is "VAL_PREREQUISITE" or "SEM_STRUCTURE") ? "Some declaration checks could not complete because structural prerequisites failed." : null);

    private static string? SelectionIssue(JsonObject root, string format, bool odcs)
    {
        if (format == "auto" && odcs && root.ContainsKey("resources")) return "Ambiguous contract standard; select --input-format explicitly.";
        if (odcs && Text(root["apiVersion"]) != "v3.2.0") return "An explicit apiVersion of v3.2.0 is required; other ODCS versions are unsupported.";
        if (!odcs && DescriptorProfiles.Select(root, odcs) is null)
            return "Unsupported Data Package profile URI. Select the official v2.0 profile or Packata Category Tables 1.0.0; profiles are never downloaded from the descriptor.";
        return null;
    }

    private static void EvaluateLayer(ValidationLayers layer, JsonObject root, string text, string format,
        DescriptorProfile profile, Uri? baseUri, List<ValidationFinding> findings)
    {
        if (layer == ValidationLayers.Schema)
        {
            var schema = DescriptorProfiles.Schema(profile);
            var instance = JsonSerializer.SerializeToElement(root);
            var evaluation = schema.Evaluate(instance,
                new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical, RequireFormatValidation = true });
            findings.AddRange(SchemaDiagnostics.Project(schema, evaluation, instance, profile));
            return;
        }
        if (profile.Format == "odcs") OdcsSemanticValidation.Validate(root, findings);
        NativeSemantics(text, format, baseUri, findings, profile.Format == "odcs");
    }

    private static void NativeSemantics(string text, string format, Uri? baseUri, List<ValidationFinding> findings, bool odcs)
    {
        try
        {
            var document = ContractDocument.Parse(text, format, baseUri);
            foreach (var finding in ContractDocument.Validate(document.NativeDocument))
                findings.Add(new(ValidationLayers.Semantic, finding.Code, finding.Severity, finding.SourcePath,
                    ContractJson.Redact(finding.Message)));
            if (!odcs) return;
            foreach (var finding in document.Map().Diagnostics.Where(f => f.Code is "ODCS004" or "ODCS005" or "ODCS_QUALITY"))
                findings.Add(new(ValidationLayers.Semantic, finding.Code,
                    finding.Code == "ODCS005" ? MappingSeverity.Error : finding.Severity,
                    finding.SourcePath, NormalizationMessage(finding.Code)));
        }
        catch (Exception exception) when (exception is YamlException or FormatException or InvalidOperationException or ArgumentException or OverflowException or NullReferenceException or NotSupportedException or InvalidCastException)
        {
            findings.Add(new(ValidationLayers.Semantic, "VAL_PREREQUISITE", MappingSeverity.Error, "$",
                "Native declaration checks could not complete; check declaration shapes and logical type options."));
        }
    }

    private static string NormalizationMessage(string code) => code switch
    {
        "ODCS005" => "SLA declaration cannot be normalized.",
        "ODCS004" => "Unsupported SLA declaration; retained without evaluation.",
        _ => "Quality declaration has normalization limitations; no rule was executed."
    };

    internal static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject Read(string text)
    {
        var root = ContractDocument.ParseRoot(text); // Bounds, duplicate YAML keys and cycles.
        if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
            return JsonNode.Parse(text) as JsonObject ?? throw new FormatException();
        return Convert(root) as JsonObject ?? throw new FormatException("A mapping is required.");
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(pair => new KeyValuePair<string, JsonNode?>(
            pair.Key is YamlScalarNode key ? key.Value ?? "" : throw new FormatException(), Convert(pair.Value)))),
        YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(Convert).ToArray()),
        YamlScalarNode scalar => Scalar(scalar),
        _ => throw new FormatException()
    };

    private static JsonNode? Scalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        var explicitTag = !scalar.Tag.IsEmpty && !scalar.Tag.IsNonSpecific;
        if (StringScalar(scalar, explicitTag)) return JsonValue.Create(value);
        if (NullScalar(value) && (!explicitTag || scalar.Tag == "tag:yaml.org,2002:null")) return null;
        return NonNullScalar(scalar, value, explicitTag);
    }

    private static bool StringScalar(YamlScalarNode scalar, bool explicitTag)
        => scalar.Tag == "tag:yaml.org,2002:str" || (!explicitTag && scalar.Style is not (ScalarStyle.Plain or ScalarStyle.Any));

    private static bool NullScalar(string value) => value is "" or "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase);

    private static JsonNode? NonNullScalar(YamlScalarNode scalar, string value, bool explicitTag)
    {
        if (bool.TryParse(value, out var boolean) && (!explicitTag || scalar.Tag == "tag:yaml.org,2002:bool")) return JsonValue.Create(boolean);
        // YAML 1.2 core numeric spellings, retaining numeric kinds rather than treating
        // quoted codes as numbers. Only JSON-compatible finite scalar values are accepted.
        if (NumericTag(scalar, explicitTag) && NumericScalar(value) is { } number) return number;
        if (explicitTag || NonFiniteNumbers.Contains(value.ToLowerInvariant())) throw new FormatException();
        return JsonValue.Create(value);
    }

    private static readonly HashSet<string> NonFiniteNumbers = [".inf", "+.inf", "-.inf", ".nan"];
    private static bool NumericTag(YamlScalarNode scalar, bool explicitTag)
        => !explicitTag || scalar.Tag == "tag:yaml.org,2002:int" || scalar.Tag == "tag:yaml.org,2002:float";

    private static JsonNode? NumericScalar(string value)
    {
        var timeout = TimeSpan.FromMilliseconds(100);
        if (Regex.IsMatch(value, @"^0[ox][0-9a-fA-F]+$", RegexOptions.None, timeout)) return RadixScalar(value);
        if (Regex.IsMatch(value, @"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$", RegexOptions.None, timeout))
        {
            var normalized = value.TrimStart('+');
            normalized = Regex.Replace(normalized, @"^(-?)0+(?=[0-9])", "$1", RegexOptions.None, timeout);
            normalized = Regex.Replace(normalized, @"^(-?)\.", "${1}0.", RegexOptions.None, timeout);
            normalized = Regex.Replace(normalized, @"\.(?=[eE]|$)", ".0", RegexOptions.None, timeout);
            return JsonNode.Parse(normalized);
        }
        return null;
    }

    private static JsonNode? RadixScalar(string value)
    {
        var number = BigInteger.Zero;
        var radix = value[1] == 'o' ? 8 : 16;
        foreach (var character in value[2..])
        {
            var digit = char.IsDigit(character) ? character - '0' : char.ToLowerInvariant(character) - 'a' + 10;
            if (digit >= radix) throw new FormatException();
            number = (number * radix) + digit;
        }
        return JsonNode.Parse(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
