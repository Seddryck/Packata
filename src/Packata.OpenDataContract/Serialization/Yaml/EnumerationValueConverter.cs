using System.Globalization;
using System.Text.RegularExpressions;
using Packata.Core.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

/// <summary>Preserves YAML enum scalar kinds without coercing quoted string codes.</summary>
internal sealed class EnumerationValueConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(EnumerationValue);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
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
                    entry.Value = parser.TryConsume<Scalar>(out var scalar) ? ReadScalar(scalar)! : deserializer(typeof(object))!;
                    break;
                case "label": entry.Label = (string?)deserializer(typeof(string)); break;
                case "description": entry.Description = (string?)deserializer(typeof(string)); break;
                case "id": entry.Id = (string?)deserializer(typeof(string)); break;
                case "tags": entry.Tags = (List<string>)deserializer(typeof(List<string>))!; break;
                case "authoritativeDefinitions": entry.AuthoritativeDefinitions = (List<AuthoritativeDefinition>)deserializer(typeof(List<AuthoritativeDefinition>))!; break;
                case "customProperties": entry.CustomProperties = (CustomProperties)deserializer(typeof(CustomProperties))!; break;
                default: parser.SkipThisAndNestedEvents(); break;
            }
        }
        return entry;
    }

    private static object? ReadScalar(Scalar scalar)
    {
        var typed = scalar.Tag == "tag:yaml.org,2002:int" || scalar.Tag == "tag:yaml.org,2002:float" ||
            scalar.Tag == "tag:yaml.org,2002:bool" || scalar.Tag == "tag:yaml.org,2002:null";
        if (scalar.Tag == "tag:yaml.org,2002:str" || !typed && scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return scalar.Value;
        if (scalar.Tag != "tag:yaml.org,2002:int" && scalar.Tag != "tag:yaml.org,2002:float")
        {
            if (scalar.Value.ToLowerInvariant() is "null" or "~" or "") return null;
            if (bool.TryParse(scalar.Value, out var boolean)) return boolean;
        }
        if (scalar.Tag != "tag:yaml.org,2002:float" && Regex.IsMatch(scalar.Value, @"^[+-]?[0-9]+$"))
        {
            if (long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
            throw new YamlException(scalar.Start, scalar.End, "Enum integer is outside the signed 64-bit range.");
        }
        if (scalar.Tag == "tag:yaml.org,2002:float" || Regex.IsMatch(scalar.Value, @"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$"))
        {
            if (ScalarValue.TryParseNumber(scalar.Value, out var number)) return number.Value;
            throw new YamlException(scalar.Start, scalar.End, "Enum number exceeds the supported decimal/floating range or precision.");
        }
        if (scalar.Value.ToLowerInvariant() is ".inf" or "+.inf" or "-.inf" or ".nan")
            throw new YamlException(scalar.Start, scalar.End, "Non-finite enum numbers are not supported.");
        if (typed) throw new YamlException(scalar.Start, scalar.End, "Invalid explicitly typed enum scalar.");
        return scalar.Value;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var entry = (EnumerationValue)value!;
        var scalar = ScalarValue.From(entry.Value);
        emitter.Emit(new MappingStart());
        emitter.Emit(new Scalar("value"));
        if (scalar.Kind == ScalarKind.String)
            emitter.Emit(new Scalar(null, null, (string)scalar.Value!, ScalarStyle.DoubleQuoted, true, false));
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
        else serializer(scalar.Value);
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
