using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Packata.Core.Storage;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeDeserializers;

namespace Packata.DataContractSpecification.Serialization.Yaml;

internal class DataContractSerializer : IDataContractSerializer
{
    public DataContract Deserialize(StreamReader reader, IDataPackageContainer container, IStorageProvider provider)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(new DataContractNamingConvention())
            .WithTypeDiscriminatingNodeDeserializer((o) =>
            {
                new ServerTypeDiscriminator().Execute(o);
            })
            .IgnoreUnmatchedProperties()
            .Build();

        var dataContract = deserializer.Deserialize<DataContract>(reader)
                          ?? throw new YamlDotNet.Core.YamlException("The YAML data is not valid.");
        return dataContract;
    }
}
