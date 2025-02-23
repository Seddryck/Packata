using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;

namespace Packata.Core.Serialization.Yaml
{
    internal class FieldConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
            => typeof(Field).IsAssignableFrom(type);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
        {
            var fields = new List<Field>();
            parser.Consume<SequenceStart>();

            while (parser.TryConsume<MappingStart>(out _))
            {
                var field = new Field();
                while (parser.TryConsume<Scalar>(out var scalar))
                {
                    var propertyName = scalar.Value;
                    if (parser.TryConsume<Scalar>(out scalar) && scalar != null)
                    {
                        var propertyValue = scalar.Value;

                        // Map properties to the field object
                        // Example: field.Name = propertyValue;
                    }
                }
                fields.Add(field);
                parser.Consume<MappingEnd>();
            }

            parser.Consume<SequenceEnd>();
            return fields;
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
