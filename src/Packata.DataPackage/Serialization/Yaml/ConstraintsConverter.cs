using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using System.Globalization;

namespace Packata.DataPackage.Serialization.Yaml;

internal class ConstraintsConverter : IYamlTypeConverter
{
    private readonly ConstraintMapper _constraintMapper = new();

    public bool Accepts(Type type)
        => type == typeof(FieldConstraintCollection);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
    {
        var list = new FieldConstraintCollection();

        parser.Consume<MappingStart>();

        while (parser.TryConsume<Scalar>(out var scalar))
        {
            if (scalar.Value == "enum")
            {
                list.Add(new EnumConstraint(ReadEnum(parser, deserializer)));
                continue;
            }
            var value = deserializer(typeof(object));
            list.Add(_constraintMapper.Map(scalar.Value, Normalize(value)));
        }

        parser.Consume<MappingEnd>();
        return list;
    }

    private static IEnumerable<object?> ReadEnum(IParser parser, ObjectDeserializer deserializer)
    {
        var values = new List<object?>();
        parser.Consume<SequenceStart>();
        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            if (parser.TryConsume<Scalar>(out var scalar))
            {
                values.Add(scalar.Tag == "tag:yaml.org,2002:str" || scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded
                    ? scalar.Value : Normalize(scalar.Value));
            }
            else
            {
                values.Add(Normalize(deserializer(typeof(object))));
            }
        }
        return values;
    }

    private static object? Normalize(object? value)
        => value switch
        {
            IDictionary<object, object> dictionary => dictionary.ToDictionary(
                pair => Convert.ToString(pair.Key)!, pair => Normalize(pair.Value)),
            IEnumerable<object> values when value is not string => values.Select(Normalize).ToArray(),
            string text when text.Equals("null", StringComparison.OrdinalIgnoreCase) || text == "~" => null,
            string text when bool.TryParse(text, out var boolean) => boolean,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
            string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => value
        };

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotImplementedException();
}
