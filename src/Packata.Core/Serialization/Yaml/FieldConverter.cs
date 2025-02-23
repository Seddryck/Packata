using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.Reflection;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;

namespace Packata.Core.Serialization.Yaml
{
    internal class FieldConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
            => type.IsAssignableFrom(typeof(List<Field>));

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
        {
            var list = new List<Field>();
            parser.Consume<SequenceStart>();

            while (parser.TryConsume<MappingStart>(out _))
            {
                var dict = new Dictionary<string, object>();
                while (parser.TryConsume<Scalar>(out var scalar))
                {
                    var propertyName = scalar.Value;
                    if (parser.TryConsume<Scalar>(out scalar) && scalar != null)
                    {
                        var propertyValue = scalar.Value;
                        dict.Add(propertyName, propertyValue);
                    }
                }

                list.Add((string)(dict["type"]) switch
                {
                    "string" => ToObject<StringField>(dict)!,
                    "number" => ToObject<NumberField>(dict)!,
                    "integer" => ToObject<IntegerField>(dict)!,
                    "date" => ToObject<DateField>(dict)!,
                    "time" => ToObject<TimeField>(dict)!,
                    "datetime" => ToObject<DateTimeField>(dict)!,
                    "year" => ToObject<YearField>(dict)!,
                    "yearmonth" => ToObject<YearMonthField>(dict)!,
                    "boolean" => ToObject<BooleanField>(dict)!,
                    "object" => ToObject<ObjectField>(dict)!,
                    _ => ToObject<Field>(dict)!,
                });

                parser.Consume<MappingEnd>();
            }

            parser.Consume<SequenceEnd>();
            return list;
        }

        private static Field ToObject<T>(Dictionary<string, object> dictionary) where T : Field
        {
            Type type = typeof(T);
            var obj = Activator.CreateInstance(type);
            foreach (var kvp in dictionary)
            {
                PropertyInfo? property = type.GetProperty(kvp.Key);
                if (property != null && property.CanWrite)
                {
                    object value = Convert.ChangeType(kvp.Value, property.PropertyType);
                    property.SetValue(obj, value);
                }
            }
            return (Field)obj;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            var fields = (List<Field>)value;
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));

            foreach (var field in fields)
            {
                emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
                // Emit field properties
                // Example: emitter.Emit(new Scalar("name")); emitter.Emit(new Scalar(field.Name));
                emitter.Emit(new MappingEnd());
            }

            emitter.Emit(new SequenceEnd());
        }
    }
}
