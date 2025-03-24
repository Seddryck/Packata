using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class DbTypeMapper : BaseTypeMapper<DbType>
{
    public DbTypeMapper()
        :base() { }

    protected override DbType DefaultMapping => DbType.Object;

    protected override void Initialize()
    {
        Register("string", null, DbType.String);
        Register("boolean", null, DbType.Boolean);
        Register("number", "fp32", DbType.Single);
        Register("number", "fp64", DbType.Double);
        Register("number", null, DbType.Decimal);
        Register("integer", "i16", DbType.Int16);
        Register("integer", "i32", DbType.Int32);
        Register("integer", "i64", DbType.Int64);
        Register("integer", null, DbType.Int32);
        Register("date", null, DbType.Date);
        Register("time", null, DbType.Time);
        Register("datetime", null, DbType.DateTime);
        Register("year", null, DbType.Int32);
        Register("yearmonth", null, DbType.String);
    }
}
