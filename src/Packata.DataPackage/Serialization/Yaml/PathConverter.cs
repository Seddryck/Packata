using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using Packata.Core.Storage;
using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace Packata.Core.Serialization.Yaml;

internal class PathConverter : IYamlTypeConverter
{
    private readonly PathFactory _factory;

    public PathConverter(PathFactory factory)
        => (_factory) = (factory);

    public bool Accepts(Type type)
        => typeof(List<IPath>).IsAssignableFrom(type);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
    {
        var paths = new List<IPath>();
        if (parser.TryConsume<SequenceStart>(out var _))
        { 
            while (parser.TryConsume<Scalar>(out var scalar))
                paths.Add(_factory.Create(scalar.Value));

            parser.Consume<SequenceEnd>();
        }
        else if(parser.TryConsume<Scalar>(out var scalar))
        {
            paths.Add(_factory.Create(scalar.Value));
        }
        else
            throw new SerializationException("Unexpected YAML format.");
        return paths;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotImplementedException();
}
