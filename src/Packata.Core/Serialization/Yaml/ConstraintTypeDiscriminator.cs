using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.Core.Serialization.Yaml;
internal class ConstraintTypeDiscriminator
{
    private static Dictionary<string, Type> GetKeyMappings()
        => new()
        {
            { "required", typeof(RequiredConstraint)},
            { "unique", typeof(UniqueConstraint)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddUniqueKeyTypeDiscriminator<Constraint>(GetKeyMappings());
}
