using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.DataPackage.Serialization.Yaml;

internal sealed class CategoryMetadataConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(CategoryTableReference) || type == typeof(CategoryFieldMap);
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var reference = type == typeof(CategoryTableReference);
        var values = ReadProperties(parser, reference);
        if (reference) return new CategoryTableReference
        {
            Resource = (string?)values.GetValueOrDefault("resource"),
            Package = (string?)values.GetValueOrDefault("package"),
            EncodedAs = (string?)values.GetValueOrDefault("encodedAs")
        };
        return new CategoryFieldMap
        {
            Value = (string?)values.GetValueOrDefault("value"),
            Label = (string?)values.GetValueOrDefault("label"),
            Labels = (List<string>?)values.GetValueOrDefault("labels"),
            Ordered = (bool?)values.GetValueOrDefault("ordered")
        };
    }

    private static Dictionary<string, object?> ReadProperties(IParser parser, bool reference)
    {
        var values = new Dictionary<string, object?>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            if (!keys.Add(key)) throw new YamlException($"Duplicate category metadata property {key}.");
            if (IsStringProperty(key, reference)) values.Add(key, ReadString(parser, key, reference));
            else if (!reference && key == "ordered") values.Add(key, ReadOrdered(parser));
            else if (!reference && key == "labels")
            {
                var labels = new List<string>();
                parser.Consume<SequenceStart>();
                while (!parser.TryConsume<SequenceEnd>(out _)) labels.Add(ReadString(parser, key, false));
                values.Add(key, labels);
            }
            else parser.SkipThisAndNestedEvents();
        }
        return values;
    }

    private static bool IsStringProperty(string key, bool reference)
        => reference ? key is "resource" or "package" or "encodedAs" : key is "value" or "label";

    private static string ReadString(IParser parser, string key, bool reference)
    {
        var scalar = parser.Consume<Scalar>();
        if (CategoriesConverter.ReadValue(scalar, 0) is string text) return text;
        throw new YamlException(scalar.Start, scalar.End, $"{(reference ? "categories" : "categoryFieldMap")}.{key}: Expected a string.");
    }

    private static bool ReadOrdered(IParser parser)
    {
        var scalar = parser.Consume<Scalar>();
        if (scalar.Style == ScalarStyle.Plain && scalar.Tag != "tag:yaml.org,2002:str" && bool.TryParse(scalar.Value, out var value))
            return value;
        throw new YamlException(scalar.Start, scalar.End, "categoryFieldMap.ordered must be boolean.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        if (value is null) { emitter.Emit(new Scalar("null")); return; }
        emitter.Emit(new MappingStart());
        if (value is CategoryTableReference reference)
        { Write(emitter, "resource", reference.Resource); Write(emitter, "package", reference.Package); Write(emitter, "encodedAs", reference.EncodedAs); }
        else if (value is CategoryFieldMap map)
        {
            Write(emitter, "value", map.Value); Write(emitter, "label", map.Label);
            if (map.Labels is not null)
            {
                emitter.Emit(new Scalar("labels"));
                emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
                foreach (var label in map.Labels)
                    emitter.Emit(new Scalar(null, null, label, ScalarStyle.DoubleQuoted, true, false));
                emitter.Emit(new SequenceEnd());
            }
            if (map.Ordered is not null)
            { emitter.Emit(new Scalar("ordered")); emitter.Emit(new Scalar(map.Ordered.Value ? "true" : "false")); }
        }
        emitter.Emit(new MappingEnd());
    }
    private static void Write(IEmitter emitter, string key, string? value)
    {
        if (value is null) return;
        emitter.Emit(new Scalar(key));
        emitter.Emit(new Scalar(null, null, value, ScalarStyle.DoubleQuoted, true, false));
    }
}
