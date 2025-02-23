using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using Packata.Core.PathHandling;

namespace Packata.Core.Serialization.Yaml
{
    internal class PathConverter : IYamlTypeConverter
    {
        private readonly HttpClient _httpClient;
        private readonly string _root;

        public PathConverter(HttpClient httpClient, string root)
            => (_httpClient, _root) = (httpClient, root);

        public bool Accepts(Type type)
            => typeof(IPath).IsAssignableFrom(type);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserializer)
        {
            var paths = new List<IPath>();
            parser.Consume<SequenceStart>();

            while (parser.TryConsume<Scalar>(out var scalar))
            {
                paths.Add(BuildPath(scalar.Value));
            }

            parser.Consume<SequenceEnd>();
            return paths;
        }

        private IPath BuildPath(string value)
            => value.Contains("://") ? new HttpPath(_httpClient, value) : new LocalPath(_root, value);

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
        {
            var paths = (List<IPath>)value;
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));

            foreach (var path in paths)
            {
                emitter.Emit(new Scalar(path.ToString()));
            }

            emitter.Emit(new SequenceEnd());
        }
    }
}
