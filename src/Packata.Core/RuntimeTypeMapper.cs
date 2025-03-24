using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class RuntimeTypeMapper : BaseTypeMapper<Type>
{
    public RuntimeTypeMapper()
        : base() { }

    protected override Type DefaultMapping => typeof(object);

    protected override void Initialize()
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
}
