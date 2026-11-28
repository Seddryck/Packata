using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Packata.Ossie;

internal static partial class OssieYaml
{
    private const int MaxDepth = 64;

    private const string TagPrefix = "tag:yaml.org,2002:";

    internal static JsonDocument Read(TextReader reader)
    {
        var yaml = new YamlStream();
        yaml.Load(new BoundedParser(new Parser(reader)));
        if (yaml.Documents.Count != 1)
            throw new OssieDocumentException("$", "Expected exactly one YAML document.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteJson(yaml.Documents[0].RootNode, writer, "$", new Traversal());
        }

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    internal static void Write(JsonElement value, TextWriter writer)
        => new YamlStream(new YamlDocument(ToYaml(value))).Save(writer, assignAnchors: false);

    private static YamlNode ToYaml(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => new YamlMappingNode(element.EnumerateObject().Select(property =>
                new KeyValuePair<YamlNode, YamlNode>(Quoted(property.Name), ToYaml(property.Value)))),
            JsonValueKind.Array => new YamlSequenceNode(element.EnumerateArray().Select(ToYaml)),
            JsonValueKind.String => Quoted(element.GetString()!),
            _ => new YamlScalarNode(element.GetRawText()) { Style = ScalarStyle.Plain },
        };

    private static YamlScalarNode Quoted(string text) => new(text) { Style = ScalarStyle.DoubleQuoted };

    private static void WriteJson(YamlNode node, Utf8JsonWriter writer, string path, Traversal traversal)
    {
        if ((writer.CurrentDepth >= MaxDepth && node is (YamlMappingNode or YamlSequenceNode)) || ++traversal.Nodes > 1_000_000)
            throw new OssieDocumentException(path, "YAML nesting or expanded alias count exceeds the document limit.");

        switch (node)
        {
            case YamlMappingNode map:
                CheckTag(map, "map", path);
                writer.WriteStartObject();
                foreach (var pair in map.Children)
                {
                    if (pair.Key is not YamlScalarNode key || !IsString(key))
                        throw new OssieDocumentException(path, "YAML mapping keys must be strings.");
                    var name = key.Value ?? string.Empty;
                    writer.WritePropertyName(name);
                    WriteJson(pair.Value, writer, OssieDocumentValidation.PropertyPath(path, name), traversal);
                }
                writer.WriteEndObject();
                break;
            case YamlSequenceNode sequence:
                CheckTag(sequence, "seq", path);
                writer.WriteStartArray();
                for (var index = 0; index < sequence.Children.Count; index++)
                    WriteJson(sequence.Children[index], writer, $"{path}/{index}", traversal);
                writer.WriteEndArray();
                break;
            case YamlScalarNode scalar:
                WriteScalar(scalar, writer, path);
                break;
            default:
                throw new OssieDocumentException(path, "Expected a JSON-compatible YAML value.");
        }
    }

    private static void CheckTag(YamlNode node, string expected, string path)
    {
        if (HasTag(node) && node.Tag != TagPrefix + expected)
            throw new OssieDocumentException(path, "Unsupported YAML tag.");
    }

    private static bool HasTag(YamlNode node) => !node.Tag.IsEmpty && !node.Tag.IsNonSpecific;

    private static bool IsString(YamlScalarNode scalar)
        => StringStyle(scalar)
            || (!HasTag(scalar) && ResolveScalar(scalar.Value ?? string.Empty) == ScalarKind.String);

    private static bool StringStyle(YamlScalarNode scalar)
        => scalar.Tag == TagPrefix + "str" || scalar.Tag == "!"
            || (!HasTag(scalar) && scalar.Style is not (ScalarStyle.Plain or ScalarStyle.Any));

    private static void WriteScalar(YamlScalarNode scalar, Utf8JsonWriter writer, string path)
    {
        var value = scalar.Value ?? string.Empty;
        var kind = StringStyle(scalar) ? ScalarKind.String : ResolveScalar(value);
        if (HasTag(scalar) && !MatchesTag(scalar, kind))
            throw new OssieDocumentException(path, "Unsupported YAML tag or invalid tagged scalar.");

        switch (kind)
        {
            case ScalarKind.String:
                writer.WriteStringValue(value);
                break;
            case ScalarKind.Null:
                writer.WriteNullValue();
                break;
            case ScalarKind.Boolean:
                writer.WriteBooleanValue(value is "true" or "True" or "TRUE");
                break;
            case ScalarKind.Integer:
            case ScalarKind.Number:
                writer.WriteRawValue(NormalizeNumber(value));
                break;
            default:
                throw new OssieDocumentException(path, "Non-finite YAML numbers cannot be represented in JSON.");
        }
    }

    private static bool MatchesTag(YamlScalarNode scalar, ScalarKind kind)
        => scalar.Tag.Value switch
        {
            TagPrefix + "str" => kind == ScalarKind.String,
            TagPrefix + "null" => kind == ScalarKind.Null,
            TagPrefix + "bool" => kind == ScalarKind.Boolean,
            TagPrefix + "int" => kind == ScalarKind.Integer,
            TagPrefix + "float" => kind is ScalarKind.Integer or ScalarKind.Number or ScalarKind.NonFinite,
            _ => false,
        };

    private static ScalarKind ResolveScalar(string value)
        => value switch
        {
            "" or "~" or "null" or "Null" or "NULL" => ScalarKind.Null,
            "true" or "True" or "TRUE" or "false" or "False" or "FALSE" => ScalarKind.Boolean,
            ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF" or "-.inf" or "-.Inf" or "-.INF"
                or ".nan" or ".NaN" or ".NAN" => ScalarKind.NonFinite,
            _ when IntegerPattern().IsMatch(value) => ScalarKind.Integer,
            _ when NumberPattern().IsMatch(value) => ScalarKind.Number,
            _ => ScalarKind.String,
        };

    private static string NormalizeNumber(string value)
    {
        if (value.StartsWith("0x", StringComparison.Ordinal))
        {
            return BigInteger.Parse("0" + value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        }

        if (value.StartsWith("0o", StringComparison.Ordinal))
        {
            var number = BigInteger.Zero;
            foreach (var digit in value.AsSpan(2))
                number = (number * 8) + (digit - '0');
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var normalized = LeadingZeroPattern().Replace(value.TrimStart('+'), "$1");
        normalized = LeadingPointPattern().Replace(normalized, "${1}0.");
        return TrailingPointPattern().Replace(normalized, ".0");
    }

    [GeneratedRegex(@"\A(?:[+-]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)\z", RegexOptions.NonBacktracking)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z", RegexOptions.NonBacktracking)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\A(-?)0+(?=[0-9])")]
    private static partial Regex LeadingZeroPattern();

    [GeneratedRegex(@"\A(-?)\.")]
    private static partial Regex LeadingPointPattern();

    [GeneratedRegex(@"\.(?=[eE]|\z)")]
    private static partial Regex TrailingPointPattern();

    private enum ScalarKind
    {
        String,
        Null,
        Boolean,
        Integer,
        Number,
        NonFinite,
    }

    // Bound nesting before YamlDotNet recursively materializes the node tree.
    private sealed class BoundedParser(IParser parser) : IParser
    {
        private int depth;

        public ParsingEvent? Current => parser.Current;

        public bool MoveNext()
        {
            if (!parser.MoveNext())
                return false;

            if (Current is MappingStart or SequenceStart)
            {
                if (++depth > MaxDepth)
                    throw new OssieDocumentException("$", $"YAML nesting exceeds {MaxDepth} levels at {Current.Start}.");
            }
            else if (Current is MappingEnd or SequenceEnd)
            {
                depth--;
            }

            return true;
        }
    }

    private sealed class Traversal
    {
        internal int Nodes { get; set; }
    }
}
