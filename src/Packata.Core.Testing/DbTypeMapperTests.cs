using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Packata.Core.Testing;
public class DbTypeMapperTests
{
    [TestCase("string", DbType.String)]
    [TestCase("boolean", DbType.Boolean)]
    [TestCase("date", DbType.Date)]
    [TestCase("integer", DbType.Int32)]
    [TestCase("number", DbType.Decimal)]
    public void Map_NoFormat_Correct(string value, DbType expected)
    {
        var mapper = new DbTypeMapper();
        var result = mapper.Map(value, null);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("integer", "i16", DbType.Int16)]
    [TestCase("number", "fp64", DbType.Double)]
    [TestCase("date", "%Y-%m-%d", DbType.Date)]
    [TestCase("date", "default", DbType.Date)]
    public void Map_Format_Correct(string value, string format, DbType expected)
    {
        var mapper = new DbTypeMapper();
        var result = mapper.Map(value, format);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Map_Default_Correct()
    {
        var mapper = new DbTypeMapper();
        var result = mapper.Map("non-existing", null);
        Assert.That(result, Is.EqualTo(DbType.Object));
    }

    [Test]
    public void Register_Timespan_Correct()
    {
        var mapper = new DbTypeMapper();
        var result = mapper.Map("timespan", null);
        Assert.That(result, Is.EqualTo(DbType.Object));

        mapper.Register("timespan", null, DbType.Time);
        result = mapper.Map("timespan", null);
        Assert.That(result, Is.EqualTo(DbType.Time));

        mapper.Register("timespan", null, DbType.Int32);
        result = mapper.Map("timespan", null);
        Assert.That(result, Is.EqualTo(DbType.Int32));
    }
}
