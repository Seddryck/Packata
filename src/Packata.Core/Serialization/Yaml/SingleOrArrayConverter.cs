using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml
{
    internal class SingleOrArrayConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
            => type == typeof(List<string>);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
        {
            var list = new List<string>();

            if (parser.TryConsume<Scalar>(out var scalar))
            {
                list.Add(scalar.Value);
                return list;
            }

            if (parser.TryConsume<SequenceStart>(out _))
            {
                while (parser.TryConsume<Scalar>(out scalar))
                {
                    list.Add(scalar.Value);
                }
                parser.Consume<SequenceEnd>();
                return list;
            }

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
