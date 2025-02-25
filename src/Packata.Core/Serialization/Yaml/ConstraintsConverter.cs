using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml
{
    internal class ConstraintsConverter : IYamlTypeConverter
    {
        private readonly ConstraintMapper constraintMapper = new();

        public bool Accepts(Type type)
            => type == typeof(List<Constraint>);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
        {
            var list = new List<Constraint>();

            parser.Consume<MappingStart>();

            while (parser.TryConsume<Scalar>(out var scalar))
            {
                var value = parser.Consume<Scalar>().Value;
                list.Add(constraintMapper.Map(scalar.Value, value));
            }

            parser.Consume<MappingEnd>();
            return list;

            throw new YamlException("Unexpected YAML format.");
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            var list = (List<string>)(value ?? throw new NullReferenceException());

            if (list.Count == 1)
            {
                emitter.Emit(new Scalar(list[0]));
            }
            else
            {
                emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
                foreach (var item in list)
                {
                    emitter.Emit(new Scalar(item));
                }
                emitter.Emit(new SequenceEnd());
            }
        }
    }
}
