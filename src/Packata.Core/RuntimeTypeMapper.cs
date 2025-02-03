using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
internal class RuntimeTypeMapper
{
    private struct TypeFormat : IEquatable<TypeFormat>
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

    private Dictionary<TypeFormat, Type> _mappings = new();

    public RuntimeTypeMapper()
    {
        Initialize();
    }

    private void Initialize()
    {
        Register("string", null, typeof(string));
        Register("boolean", null, typeof(bool));
        Register("number", "fp32", typeof(float));
        Register("number", "fp64", typeof(double));
        Register("number", null, typeof(decimal));
        Register("integer", "i16", typeof(short));
        Register("integer", "i32", typeof(int));
        Register("integer", "i64", typeof(long));
        Register("integer", null, typeof(int));
        Register("date", null, typeof(DateOnly));
        Register("time", null, typeof(TimeOnly));
        Register("datetime", null, typeof(DateTime));
        Register("year", null, typeof(int));
        Register("yearmonth", null, typeof(string));
    }

    public void Register(string type, Type runtimeType)
        => Register(type, null, runtimeType);

    public void Register(string type, string? format, Type runtimeType)
    {
        var key = new TypeFormat(type, format);
        if (!_mappings.TryAdd(key, runtimeType))
            _mappings[key] = runtimeType;
    }


    public Type Map(string? type, string? format)
    {
        if (type is not null)
        {
            var key = new TypeFormat(type, format);
            if (_mappings.TryGetValue(key, out var runtimeType))
                return runtimeType;
            else if (_mappings.TryGetValue(new TypeFormat(type, null), out runtimeType))
                return runtimeType;
        }
        return typeof(object);
    }
}
