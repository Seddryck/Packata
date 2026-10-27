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
        return ((QualityRuleDocument)rootDeserializer(typeof(QualityRuleDocument))!).ToRule();
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
            else if (operand is null) continue;
            emitter.Emit(new Scalar(char.ToLowerInvariant(property.Name[0]) + property.Name[1..]));
            serializer(operand);
        }
        emitter.Emit(new MappingEnd());
    }
}

internal sealed class QualityRuleDocument : DataQualityRule
{
    internal DataQualityRule ToRule()
    {
        var result = new DataQualityRule();
        foreach (var property in typeof(DataQualityRule).GetProperties().Where(p => p.CanWrite))
        {
            if (property.Name.StartsWith("Must", StringComparison.Ordinal)
                && !DeclaredExpectations.ContainsKey(property.Name)) continue;
            property.SetValue(result, property.GetValue(this));
        }
        return result;
    }
}
