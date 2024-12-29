using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Events;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;
internal class FieldTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => typeof(Field).IsAssignableFrom(type);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var fields = new List<Field>();
        var deserializer = new DeserializerBuilder().Build();

        parser.Consume<SequenceStart>();
        while (parser.TryConsume<MappingStart>(out _))
        {
            var fieldMap = deserializer.Deserialize<Dictionary<string, object>>(parser);
            var typeValue = fieldMap["type"]?.ToString() ?? string.Empty;

            fields.Add(typeValue switch
            {
                "string" => DeserializeField<StringField>(fieldMap),
                "number" => DeserializeField<NumberField>(fieldMap),
                "integer" => DeserializeField<IntegerField>(fieldMap),
                "date" => DeserializeField<DateField>(fieldMap),
                "time" => DeserializeField<TimeField>(fieldMap),
                "datetime" => DeserializeField<DateTimeField>(fieldMap),
                "year" => DeserializeField<YearField>(fieldMap),
                "yearmonth" => DeserializeField<YearMonthField>(fieldMap),
                "boolean" => DeserializeField<BooleanField>(fieldMap),
                "object" => DeserializeField<ObjectField>(fieldMap),
                _ => DeserializeField<Field>(fieldMap),
            });
        }
        parser.Consume<SequenceEnd>();
        return fields;
    }

    private T DeserializeField<T>(Dictionary<string, object> fieldMap)
    {
        var serializer = new SerializerBuilder().Build();
        var yaml = serializer.Serialize(fieldMap);
        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<T>(yaml);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var itemSerializer = new SerializerBuilder().Build();
        var yaml = itemSerializer.Serialize(value);
        emitter.Emit(new Scalar(yaml));
    }
}
