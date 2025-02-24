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
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithAttributeOverride(typeof(DataPackage), "Profile", new YamlMemberAttribute { Alias = "$schema" })
                .WithAttributeOverride(typeof(DataPackage), "Paths", new YamlMemberAttribute { Alias = "path" })
                .WithAttributeOverride(typeof(Resource), "Profile", new YamlMemberAttribute { Alias = "$schema" })
                .WithAttributeOverride(typeof(Resource), "Paths", new YamlMemberAttribute { Alias = "path" })
                .WithAttributeOverride(typeof(Schema), "Profile", new YamlMemberAttribute { Alias = "$schema" })
                .WithAttributeOverride(typeof(TableDialect), "Profile", new YamlMemberAttribute { Alias = "$schema" })
                .WithTypeConverter(new PathConverter(httpClient, root))
                .WithTypeConverter(new FieldConverter())
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
