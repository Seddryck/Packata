using System.Globalization;
using Packata.Core.Parsing;
using Packata.Core.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

/// <summary>Preserves YAML enum scalar kinds without coercing quoted string codes.</summary>
internal sealed class EnumerationValueConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(EnumerationValue);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();
        var entry = new EnumerationValue { Value = null! };
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            if (!keys.Add(key.Value)) throw new YamlException(key.Start, key.End, $"Duplicate enum property '{key.Value}'.");
            switch (key.Value)
            {
                case "value":
                    entry.Value = parser.TryConsume<Scalar>(out var scalar) ? ReadScalar(scalar)! : rootDeserializer(typeof(object))!;
                    break;
                case "label": entry.Label = (string?)rootDeserializer(typeof(string)); break;
                case "description": entry.Description = (string?)rootDeserializer(typeof(string)); break;
                case "id": entry.Id = (string?)rootDeserializer(typeof(string)); break;
                case "tags": entry.Tags = (List<string>)rootDeserializer(typeof(List<string>))!; break;
                case "authoritativeDefinitions": entry.AuthoritativeDefinitions = (List<AuthoritativeDefinition>)rootDeserializer(typeof(List<AuthoritativeDefinition>))!; break;
                case "customProperties": entry.CustomProperties = (CustomProperties)rootDeserializer(typeof(CustomProperties))!; break;
                default: parser.SkipThisAndNestedEvents(); break;
            }
        }
        return entry;
    }

    private const string IntegerTag = "tag:yaml.org,2002:int";
    private const string FloatTag = "tag:yaml.org,2002:float";
    private const string BooleanTag = "tag:yaml.org,2002:bool";
    private const string NullTag = "tag:yaml.org,2002:null";
    private const string StringTag = "tag:yaml.org,2002:str";

    internal static object? ReadScalar(Scalar scalar)
    {
        if (IsString(scalar)) return scalar.Value;
        if (TryReadNonNumeric(scalar, out var value)) return value;
        if (scalar.Tag != FloatTag && ScalarSyntax.IsInteger(scalar.Value)) return ReadInteger(scalar);
        if (scalar.Tag == FloatTag || ScalarSyntax.IsNumber(scalar.Value)) return ReadNumber(scalar);
        ValidateTextFallback(scalar);
        return scalar.Value;
    }

    private static bool IsString(Scalar scalar)
        => scalar.Tag == StringTag || (!HasExplicitScalarType(scalar) &&
            scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded);

    private static bool HasExplicitScalarType(Scalar scalar)
        => scalar.Tag == IntegerTag || scalar.Tag == FloatTag || scalar.Tag == BooleanTag || scalar.Tag == NullTag;

    private static bool TryReadNonNumeric(Scalar scalar, out object? value)
    {
        value = null;
        if (scalar.Tag == IntegerTag || scalar.Tag == FloatTag) return false;
        if (scalar.Value.ToLowerInvariant() is "null" or "~" or "") return true;
        if (!bool.TryParse(scalar.Value, out var boolean)) return false;
        value = boolean;
        return true;
    }

    private static long ReadInteger(Scalar scalar)
    {
        if (long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        throw new YamlException(scalar.Start, scalar.End, "Enum integer is outside the signed 64-bit range.");
    }

    private static object? ReadNumber(Scalar scalar)
    {
        if (ScalarValue.TryParseNumber(scalar.Value, out var number)) return number.Value;
        throw new YamlException(scalar.Start, scalar.End, "Enum number exceeds the supported decimal/floating range or precision.");
    }

    private static void ValidateTextFallback(Scalar scalar)
    {
        if (scalar.Value.ToLowerInvariant() is ".inf" or "+.inf" or "-.inf" or ".nan")
            throw new YamlException(scalar.Start, scalar.End, "Non-finite enum numbers are not supported.");
        if (HasExplicitScalarType(scalar)) throw new YamlException(scalar.Start, scalar.End, "Invalid explicitly typed enum scalar.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var entry = (EnumerationValue)value!;
        var scalar = ScalarValue.From(entry.Value);
        emitter.Emit(new MappingStart());
        emitter.Emit(new Scalar("value"));
        if (scalar.Kind == ScalarKind.String)
        {
            emitter.Emit(new Scalar(null, null, (string)scalar.Value!, ScalarStyle.DoubleQuoted, true, false));
        }
        else if (scalar.Kind == ScalarKind.Number)
        {
            var text = scalar.Value switch
            {
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                float number => number.ToString("R", CultureInfo.InvariantCulture),
                _ => ((decimal)scalar.Value!).ToString(CultureInfo.InvariantCulture)
            };
            if (text.IndexOfAny(['.', 'e', 'E']) < 0) text += ".0";
            emitter.Emit(new Scalar(text));
        }
        else
        {
            serializer(scalar.Value);
        }

        Write("label", entry.Label); Write("description", entry.Description); Write("id", entry.Id);
        if (entry.Tags.Count > 0) Write("tags", entry.Tags);
        if (entry.AuthoritativeDefinitions.Count > 0) Write("authoritativeDefinitions", entry.AuthoritativeDefinitions);
        if (entry.CustomProperties.Count > 0) Write("customProperties", entry.CustomProperties);
        emitter.Emit(new MappingEnd());

        void Write(string key, object? item)
        {
            if (item is null) return;
            emitter.Emit(new Scalar(key)); serializer(item);
        }
    }
}
