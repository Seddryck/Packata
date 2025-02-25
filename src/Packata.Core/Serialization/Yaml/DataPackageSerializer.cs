using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeDeserializers;

namespace Packata.Core.Serialization.Yaml
{
    internal class DataPackageSerializer : IDataPackageSerializer
    {
        public DataPackage Deserialize(StreamReader reader, HttpClient httpClient, string root)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(new DataPackageNamingConvention())
                .WithTypeConverter(new PathConverter(httpClient, root))
                .WithTypeDiscriminatingNodeDeserializer((o) => new FieldTypeDiscriminator().Execute(o))
                .WithTypeConverter(new MissingValuesConverter())
                .WithTypeConverter(new SingleOrArrayConverter())
                .IgnoreUnmatchedProperties()
                .Build();

            var dataPackage = deserializer.Deserialize<DataPackage>(reader)
                              ?? throw new YamlDotNet.Core.YamlException("The YAML data is not valid.");
            return dataPackage;
        }
    }
}
