using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;

internal class ConstraintsConverter : IYamlTypeConverter
{
    private readonly ConstraintMapper constraintMapper = new();

    public bool Accepts(Type type)
        => type == typeof(FieldConstraintCollection);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
    {
        var list = new FieldConstraintCollection();

        parser.Consume<MappingStart>();

        while (parser.TryConsume<Scalar>(out var scalar))
        {
            if (parser.TryConsume<Scalar>(out var valueScalar))
                list.Add(constraintMapper.Map(scalar.Value, valueScalar.Value));
            else
                throw new YamlException($"Expected scalar value for constraint '{scalar.Value}'");
        }

        parser.Consume<MappingEnd>();
        return list;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotImplementedException();
}
