using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.Core.Serialization.Yaml;
internal class FieldTypeDiscriminator : ITypeDiscriminator
{
    private static Dictionary<string, Type> GetValueMappings()
        => new()
        {
            { "string", typeof(StringField)},
            { "number", typeof(NumberField)},
            { "integer", typeof(IntegerField)},
            { "date", typeof(DateField)},
            { "time", typeof(TimeField)},
            { "datetime", typeof(DateTimeField)},
            { "year", typeof(YearField)},
            { "yearmonth", typeof(YearMonthField)},
            { "boolean", typeof(BooleanField)},
            { "object", typeof(ObjectField)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<Field>("type", GetValueMappings());
}
