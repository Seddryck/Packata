using System.Reflection;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

/// <summary>Preserves operand presence, including explicit null and explicitly empty ranges.</summary>
internal sealed class QualityRuleConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(DataQualityRule);
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var rule = new DataQualityRule();
        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            var property = typeof(DataQualityRule).GetProperties().SingleOrDefault(p =>
                p.CanWrite && char.ToLowerInvariant(p.Name[0]) + p.Name[1..] == key);
            if (property is null) { rootDeserializer(typeof(object)); continue; }
            // YAML operands and arguments must retain scalar types, including quoted numeric strings.
            // The default object deserializer otherwise turns unquoted numbers and booleans into strings.
            var value = property.Name.StartsWith("Must", StringComparison.Ordinal) || property.Name == nameof(DataQualityRule.Arguments)
                ? ReadValue(parser, rootDeserializer) : rootDeserializer(property.PropertyType);
            property.SetValue(rule, value);
        }
        return rule;
    }

    private static object? ReadValue(IParser parser, ObjectDeserializer fallback, int depth = 0)
    {
        if (depth > 64) throw new YamlException("Quality values exceed the supported nesting depth.");
        if (parser.TryConsume<Scalar>(out var scalar)) return EnumerationValueConverter.ReadScalar(scalar);
        if (parser.TryConsume<SequenceStart>(out _))
        {
            var values = new List<object?>();
            while (!parser.TryConsume<SequenceEnd>(out _)) values.Add(ReadValue(parser, fallback, depth + 1));
            return values;
        }
        if (parser.TryConsume<MappingStart>(out _))
        {
            var values = new Dictionary<string, object?>();
            while (!parser.TryConsume<MappingEnd>(out _))
                values.Add(parser.Consume<Scalar>().Value, ReadValue(parser, fallback, depth + 1));
            return values;
        }
        return fallback(typeof(object));
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var rule = (DataQualityRule)value!;
        emitter.Emit(new MappingStart());
        foreach (var property in typeof(DataQualityRule).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == nameof(DataQualityRule.DeclaredExpectations)) continue;
            var operand = property.GetValue(rule);
            if (property.Name.StartsWith("Must", StringComparison.Ordinal))
            {
                if (!rule.DeclaredExpectations.ContainsKey(property.Name)
                    && operand is not System.Collections.ICollection { Count: > 0 }) continue;
            }
            else if (operand is null)
            {
                continue;
            }

            emitter.Emit(new Scalar(char.ToLowerInvariant(property.Name[0]) + property.Name[1..]));
            serializer(operand);
        }
        emitter.Emit(new MappingEnd());
    }
}
