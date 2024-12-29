using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;
internal class YamlSerializerFactory
{
    public ISerializer CreateSerializer(HttpClient httpClient, string root)
        => new SerializerBuilder()
            .WithNamingConvention(new DataPackageNamingConvention())
            .WithTypeConverter(new PathTypeConverter(httpClient, root))
            .WithTypeConverter(new FieldTypeConverter())
            .WithTypeConverter(new SingleOrArrayConverter<IPath>())
            .WithTypeConverter(new SingleOrArrayConverter<string>())
            .Build();

    public IDeserializer CreateDeserializer(HttpClient httpClient, string root)
        => new DeserializerBuilder()
            .WithNamingConvention(new DataPackageNamingConvention())
            //.WithTypeConverter(new SingleOrArrayConverter<IPath>())
            .WithTypeConverter(new SingleOrArrayConverter<string>())
            .WithTypeConverter(new PathTypeConverter(httpClient, root))
            .WithTypeConverter(new FieldTypeConverter())
            .Build();
}
