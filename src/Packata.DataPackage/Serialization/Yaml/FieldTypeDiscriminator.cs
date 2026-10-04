using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.DataPackage.Serialization.Yaml;
internal interface ITypeDiscriminator
{
    void Execute(ITypeDiscriminatingNodeDeserializerOptions options);
}

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
            { "object", typeof(ObjectField)},
            { "geopoint", typeof(GeoPointField)},
            { "geojson", typeof(GeoJsonField)},
            { "array", typeof(ArrayField)},
            { "duration", typeof(DurationField)},
            { "any", typeof(AnyField)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<Field>("type", GetValueMappings());
}
