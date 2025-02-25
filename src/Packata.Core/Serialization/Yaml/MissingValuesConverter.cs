using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml
{
    internal class MissingValuesConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
            => typeof(List<MissingValue>).IsAssignableFrom(type);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
        {
            var result = new List<MissingValue>();
            parser.Consume<SequenceStart>();

            while (parser.TryConsume<Scalar>(out var scalar) || parser.TryConsume<MappingStart>(out _))
            {
                if (scalar is not null)
                {
                    result.Add(new MissingValue(scalar.Value, null));
                    continue;
                }

                string? value = null;
                string? label = null;

                while (parser.TryConsume<Scalar>(out var key))
                {
                    if (key.Value == "value" && parser.TryConsume<Scalar>(out var scalarValue))
                    {
                        value = scalarValue.Value;
                    }
                    else if (key.Value == "label" && parser.TryConsume<Scalar>(out var scalarLabel))
                    {
                        label = scalarLabel.Value;
                    }
                }

                result.Add(new MissingValue(value ?? throw new ArgumentException(), label));
                parser.Consume<MappingEnd>();
            }
            parser.TryConsume<SequenceEnd>(out var _);

            return result;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
            => throw new NotImplementedException();
    }
}
