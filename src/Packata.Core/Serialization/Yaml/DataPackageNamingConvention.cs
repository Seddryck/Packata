using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;
internal class DataPackageNamingConvention : INamingConvention
{
    public string Apply(string value)
    {
        value = value.ToCamelCase();
        value = value == "profile" ? "$schema" : value;
        value = value == "paths" ? "path" : value;
        return value;
    }

    public string Reverse(string value)
    {
        value = value.ToPascalCase();
        value = value == "$schema" ? "profile" : value;
        value = value == "path" ? "paths" : value;
        return value;
    }
}
