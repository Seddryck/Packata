using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.Core.Serialization.Yaml;
internal interface ITypeDiscriminator
{
    void Execute(ITypeDiscriminatingNodeDeserializerOptions options);
}
