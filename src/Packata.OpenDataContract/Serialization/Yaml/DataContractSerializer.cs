using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Packata.Core.Storage;
using Packata.OpenDataContract.ServerTypes;
using Packata.OpenDataContract.Validation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeDeserializers;

namespace Packata.OpenDataContract.Serialization.Yaml;

internal class DataContractSerializer : IDataContractSerializer
{
    public DataContract Deserialize(StreamReader reader, IDocumentContainer container, IStorageProvider provider)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(new DataContractNamingConvention())
            .WithTypeDiscriminatingNodeDeserializer((o) =>
            {
                new ServerTypeDiscriminator().Execute(o);
            })
            .WithTypeConverter(new TeamConverter())
            .WithTypeConverter(new CustomPropertyListConverter())
            .IncludeNonPublicProperties()
            .WithAttributeOverride(typeof(SchemaProperty), nameof(SchemaProperty.LogicalType), new YamlIgnoreAttribute())
            .WithAttributeOverride(typeof(SchemaProperty), "LogicalTypeDiscriminator", new YamlMemberAttribute() { Alias = "logicalType" })
            .IgnoreUnmatchedProperties()
            .Build();

        var dataContract = deserializer.Deserialize<DataContract>(reader)
                          ?? throw new YamlDotNet.Core.YamlException("The YAML data is not valid.");
        DataContractValidator.Validate(dataContract);
        return dataContract;
    }

    public string Serialize(DataContract dataContract)
    {
        ArgumentNullException.ThrowIfNull(dataContract);

        return new SerializerBuilder()
            .WithNamingConvention(new DataContractNamingConvention())
            .WithTypeConverter(new TeamConverter())
            .WithTypeConverter(new CustomPropertyListConverter())
            .IncludeNonPublicProperties()
            .WithAttributeOverride(typeof(SchemaProperty), nameof(SchemaProperty.LogicalType), new YamlIgnoreAttribute())
            .WithAttributeOverride(typeof(SchemaProperty), "LogicalTypeDiscriminator", new YamlMemberAttribute { Alias = "logicalType" })
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build()
            .Serialize(dataContract);
    }
}
