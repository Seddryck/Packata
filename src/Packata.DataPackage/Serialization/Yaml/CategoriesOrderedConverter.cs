using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.DataPackage.Serialization.Yaml;

internal sealed class CategoriesOrderedConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(bool?);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        if (scalar.Style == ScalarStyle.Plain && scalar.Tag != "tag:yaml.org,2002:str" && bool.TryParse(scalar.Value, out var value))
            return value;
        throw new YamlException(scalar.Start, scalar.End, "categoriesOrdered must be boolean when present.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => emitter.Emit(new Scalar(value switch { null => "null", true => "true", _ => "false" }));
}
