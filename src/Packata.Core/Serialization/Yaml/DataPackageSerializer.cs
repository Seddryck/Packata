using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.Core.Serialization.Yaml
{
    internal class DataPackageSerializer : IDataPackageSerializer
    {
        public DataPackage Deserialize(StreamReader reader, HttpClient httpClient, string root)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new PathConverter(httpClient, root))
                .WithTypeConverter(new FieldConverter())
                .WithTypeConverter(new MissingValuesConverter())
                .WithTypeConverter(new SingleOrArrayConverter())
                .Build();

            var dataPackage = deserializer.Deserialize<DataPackage>(reader)
                              ?? throw new YamlDotNet.Core.YamlException("The YAML data is not valid.");
            return dataPackage;
        }
    }
}
