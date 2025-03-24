using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public abstract class BaseTypeMapper<T>
{
    protected struct TypeFormat : IEquatable<TypeFormat>
    {
        public TypeFormat(string type, string? format)
        {
            Type = type;
            Format = format;
        }

        public string Type { get; set; }
        public string? Format { get; set; }
        public bool Equals(TypeFormat other)
            => Type == other.Type && (Format == other.Format
                    || (other.Format is null && Format is null)
            );
    }

    protected Dictionary<TypeFormat, T> Mappings { get; } = [];
    protected abstract T DefaultMapping { get; }

    public BaseTypeMapper()
    {
        Initialize();
    }

    protected abstract void Initialize();
    public void Register(string type, T runtimeType)
        => Register(type, null, runtimeType);

    public void Register(string type, string? format, T runtimeType)
    {
        var key = new TypeFormat(type, format);
        if (!Mappings.TryAdd(key, runtimeType))
            Mappings[key] = runtimeType;
    }

    public T Map(string? type, string? format)
    {
        if (type is not null)
        {
            var key = new TypeFormat(type, format);
            if (Mappings.TryGetValue(key, out var runtimeType))
                return runtimeType;
            else if (Mappings.TryGetValue(new TypeFormat(type, null), out runtimeType))
                return runtimeType;
        }
        return DefaultMapping;
    }
}
