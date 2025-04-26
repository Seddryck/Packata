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

namespace Packata.Core.Serialization.Yaml;

internal class DataPackageSerializer : IDataPackageSerializer
{
    public DataPackage Deserialize(StreamReader reader, IDataPackageContainer container)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(new DataPackageNamingConvention())
            
            .WithTypeDiscriminatingNodeDeserializer((o) =>
                {
                    new FieldTypeDiscriminator().Execute(o);
                    new TableDialectTypeDiscriminator().Execute(o);
                })
            .WithObjectFactory(new ResourcesObjectFactory(container.BaseUri.ToString()))
            .WithTypeConverter(new PathConverter(new PathFactory(container)))
            .WithTypeConverter(new MissingValuesConverter())
            .WithTypeConverter(new SingleOrArrayConverter())
            .WithTypeConverter(new ConstraintsConverter())
            .WithTypeConverter(new CategoriesConverter())
            .WithTypeConverter(new ConnectionConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        var dataPackage = deserializer.Deserialize<DataPackage>(reader)
                          ?? throw new YamlDotNet.Core.YamlException("The YAML data is not valid.");
        return dataPackage;
    }
}
