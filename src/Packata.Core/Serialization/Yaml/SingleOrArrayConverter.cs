using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Events;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Packata.Core.Serialization.Yaml;
internal class SingleOrArrayConverter<T> : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(List<T>);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar scalar)
        {
            // Single string case
            var value = scalar.Value ?? string.Empty;
            parser.MoveNext();
            return new List<T> { (T)(object)value };
        }

        if (parser.Current is SequenceStart)
        {
            // Array case
            var list = new List<T>();
            parser.MoveNext();

            while (parser.Current is Scalar itemScalar)
            {
                list.Add((T)(object)(itemScalar.Value ?? string.Empty));
                parser.MoveNext();
            }

            // Consume SequenceEnd
            if (parser.Current is SequenceEnd)
                parser.MoveNext();

            return list;
        }

        throw new InvalidOperationException("Unexpected YAML format.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotImplementedException();
        //var list = (List<T>)value;

        //if (list.Count == 1)
        //{
        //    // Write as a single scalar
        //    emitter.Emit(new Scalar(list[0].ToString()));
        //}
        //else
        //{
        //    // Write as a sequence
        //    emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
        //    foreach (var item in list)
        //    {
        //        emitter.Emit(new Scalar(item.ToString()));
        //    }
        //    emitter.Emit(new SequenceEnd());
        //}
    }
}
