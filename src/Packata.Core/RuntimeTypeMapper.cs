using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
internal class RuntimeTypeMapper
{
    public static Type Map(string? type, string? format)
        => type switch
        {
            "string" => typeof(string),
            "boolean" => typeof(bool),
            "number" => format switch
            {
                "fp32" => typeof(float),
                "fp64" => typeof(double),
                _ => typeof(decimal)
            },
            "integer" => format switch
            {
                "i16" => typeof(short),
                "i32" => typeof(int),
                "i64" => typeof(long),
                _ => typeof(int)
            },
            "date" => typeof(DateOnly),
            "time" => typeof(TimeOnly),
            "datetime" => typeof(DateTime),
            _ => typeof(object)
        };
}
