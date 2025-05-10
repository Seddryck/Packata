using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization;
using SerJson = Packata.Core.Serialization.Json;
using SerYaml = Packata.Core.Serialization.Yaml;
using Packata.Core.Storage;

namespace Packata.Core.Testing.Serialization;

public class ExtensionSerializerTests
{
    private static Stream GetDataPackageProperties(string format)
    {
        var uformat = format.ToUpper()[0] + format.Substring(1);
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Serialization.{uformat}.Resources.extension.{format}";
        var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");
        return stream;
    }

    private static IDataPackageSerializer GetSerializer(string format)
        => format switch
        {
            "json" => new SerJson.DataPackageSerializer(),
            "yaml" => new SerYaml.DataPackageSerializer(),
            _ => throw new NotSupportedException($"The format '{format}' is not supported.")
        };

    public static IEnumerable<(Stream stream, IDataPackageSerializer serializer)> GetData()
    {
        yield return (GetDataPackageProperties("json"), GetSerializer("json"));
        yield return (GetDataPackageProperties("yaml"), GetSerializer("yaml"));
    }

    [TestCaseSource(nameof(GetData))]
    public void Deserialize_Package_Success((Stream Stream, IDataPackageSerializer Serializer) value)
    {
        using var streamReader = new StreamReader(value.Stream);
        var dataPackage = value.Serializer.Deserialize(streamReader, new LocalDirectoryDataPackageContainer(), new StorageProvider());
        Assert.That(dataPackage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Name, Is.EqualTo("extension_package"));
        });
    }


    [TestCaseSource(nameof(GetData))]
    public void Deserialize_ResourceKind_Success((Stream Stream, IDataPackageSerializer Serializer) value)
    {
        using var streamReader = new StreamReader(value.Stream);
        var dataPackage = value.Serializer.Deserialize(streamReader, new LocalDirectoryDataPackageContainer(), new StorageProvider());
        Assert.That(dataPackage.Resources, Is.Not.Null);
        Assert.That(dataPackage.Resources, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Resources[0].Kind, Is.EqualTo("local"));
        });
    }


    [TestCaseSource(nameof(GetData))]
    public void Deserialize_Metrics_Success((Stream Stream, IDataPackageSerializer Serializer) value)
    {
        using var streamReader = new StreamReader(value.Stream);
        var dataPackage = value.Serializer.Deserialize(streamReader, new LocalDirectoryDataPackageContainer(), new StorageProvider());
        Assert.That(dataPackage.Resources[0].Schema?.Metrics, Is.Not.Null);
        var metrics = dataPackage.Resources[0].Schema?.Metrics;
        Assert.Multiple(() =>
        {
            Assert.That(metrics, Has.Count.EqualTo(2));
            Assert.That(metrics?[0].Name, Is.EqualTo("average_temperature"));
            Assert.That(metrics?[0].Type, Is.EqualTo("numeric"));
            Assert.That(metrics?[0].Title, Is.EqualTo("Average temperature"));
            Assert.That(metrics?[0].Description, Is.EqualTo("Average temperature of the sensor"));
            Assert.That(metrics?[0].Aggregation, Is.EqualTo("avg"));
            Assert.That(metrics?[0].Expression, Is.EqualTo("temperature"));
        });
    }
}
