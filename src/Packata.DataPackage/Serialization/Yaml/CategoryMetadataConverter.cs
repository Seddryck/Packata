using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.DataPackage.Serialization.Yaml;

internal sealed class CategoryMetadataConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(CategoryTableReference) || type == typeof(CategoryFieldMap);
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
    {
        var reference = type == typeof(CategoryTableReference);
        var values = new Dictionary<string, string>();
        bool? ordered = null;
        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            if (values.ContainsKey(key)) throw new YamlException($"Duplicate category metadata property {key}.");
            if ((reference && key is "resource" or "package" or "encodedAs") || (!reference && key is "value" or "label"))
            {
                var scalar = parser.Consume<Scalar>();
                if (CategoriesConverter.ReadValue(scalar, 0) is not string text)
                    throw new YamlException(scalar.Start, scalar.End, $"{(reference ? "categories" : "categoryFieldMap")}.{key}: Expected a string.");
                values.Add(key, text);
            }
            else if (!reference && key == "ordered")
            {
                var scalar = parser.Consume<Scalar>();
                if (ordered is not null || scalar.Style != ScalarStyle.Plain || !bool.TryParse(scalar.Value, out var boolean) || scalar.Tag == "tag:yaml.org,2002:str")
                    throw new YamlException(scalar.Start, scalar.End, "categoryFieldMap.ordered must be boolean.");
                ordered = boolean;
            }
            else parser.SkipThisAndNestedEvents();
        }
        return reference ? new CategoryTableReference
        { Resource = values.GetValueOrDefault("resource"), Package = values.GetValueOrDefault("package"), EncodedAs = values.GetValueOrDefault("encodedAs") }
        : new CategoryFieldMap { Value = values.GetValueOrDefault("value"), Label = values.GetValueOrDefault("label"), Ordered = ordered };
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
