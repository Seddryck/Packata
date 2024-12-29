using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.PathHandling;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;
internal class PathTypeConverter : IYamlTypeConverter
{
    private readonly HttpClient _httpClient;
    private readonly string _root;

    public PathTypeConverter(HttpClient httpClient, string root)
    {
        _httpClient = httpClient;
        _root = root;
    }

    public bool Accepts(Type type) => type == typeof(List<IPath>);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar scalar)
        {
            // Single string case
            var value = BuildPath(scalar.Value ?? string.Empty);
            parser.MoveNext();
            return new List<IPath> { value };
        }

        if (parser.Current is SequenceStart)
        {
            // Array case
            var list = new List<IPath>();
            parser.MoveNext();

            while (parser.Current is Scalar itemScalar)
            {
                list.Add(BuildPath(itemScalar.Value ?? string.Empty));
                parser.MoveNext();
            }

            // Consume SequenceEnd
            if (parser.Current is SequenceEnd)
                parser.MoveNext();

            return list;
        }

        throw new InvalidOperationException("Unexpected YAML format.");
    }

    public IPath BuildPath(string value)
        => value.Contains("://") ? new HttpPath(_httpClient, value) : new LocalPath(_root, value);

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var path = (value as IPath)?.ToString() ?? string.Empty;
        emitter.Emit(new Scalar(path));
    }
}
