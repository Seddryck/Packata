using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization.ObjectFactories;

namespace Packata.Core.Serialization.Yaml;
internal class ResourcesObjectFactory : DefaultObjectFactory
{
    private string RootPath { get; }

    public ResourcesObjectFactory(string rootPath)
        => RootPath = rootPath;

    public override object Create(Type type)
    {
        if (type == typeof(Resource))
            return new Resource(RootPath);
        return base.Create(type);
    }
}
