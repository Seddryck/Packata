using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization.Yaml;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;
internal class ResourcesObjectFactoryTests : BaseObjectFactoryTests<ResourcesObjectFactory, List<Resource>>
{
    public ResourcesObjectFactoryTests()
        : base("resources")
    { }

    protected override ResourcesObjectFactory CreateObjectFactory()
        => new ("c:\\");

    [Test]
    public void ReadYaml_ValidYamlString_ReturnsCorrectResources()
    {
        var json = @"
                    resources:
                     - name: resource_01
                     - name: resource_02
                     - name: resource_03
                ";

        var wrapper = Deserializer.Deserialize<Wrapper>(json);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Is.All.InstanceOf<Resource>());
            Assert.That(wrapper.Object, Has.All.Property("RootPath").EqualTo("c:\\"));
        }
    }
}
