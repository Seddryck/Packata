using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using Packata.Core.PathHandling;
using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace Packata.Core.Serialization.Yaml;

internal class ConnectionConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
        => typeof(IConnection).IsAssignableFrom(type);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new LiteralConnectionUrl(scalar.Value);
        }
        throw new SerializationException("Unexpected YAML format.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotImplementedException();
}
