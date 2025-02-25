using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using PocketCsvReader;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;

internal class CategoriesConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
        => type == typeof(List<ICategory>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
    {
        parser.Consume<SequenceStart>();

        var list = new List<ICategory>();
        while (!parser.TryConsume<SequenceEnd>(out var _))
        {
            if (parser.TryConsume<Scalar>(out var valueScalar))
                list.Add(new CategoryLabel(valueScalar.Value));
            else if (parser.TryConsume<MappingStart>(out var _))
            {
                int? value = null;
                string? label = null;
                while (parser.TryConsume<Scalar>(out var key))
                {
                    if (key.Value == "value" && parser.TryConsume<Scalar>(out var scalarValue))
                        value = Convert.ToInt32(scalarValue.Value);
                    else if (key.Value == "label" && parser.TryConsume<Scalar>(out var scalarLabel))
                        label = scalarLabel.Value;
                }
                if (value.HasValue && label is not null)
                    list.Add(new Category(value.Value, label));
                parser.Consume<MappingEnd>();
            }
        }

        return list;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotImplementedException();
}
