using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Packata.Core.Testing;
public class RuntimeTypeMapperTests
{
    [TestCase("string", typeof(string))]
    [TestCase("boolean", typeof(bool))]
    [TestCase("date", typeof(DateOnly))]
    [TestCase("integer", typeof(int))]
    [TestCase("number", typeof(decimal))]
    public void Map_NoFormat_Correct(string value, Type expected)
    {
        var mapper = new RuntimeTypeMapper();
        var result = mapper.Map(value, null);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("integer", "i16", typeof(short))]
    [TestCase("number", "fp64", typeof(double))]
    [TestCase("date", "%Y-%m-%d", typeof(DateOnly))]
    [TestCase("date", "default", typeof(DateOnly))]
    public void Map_Format_Correct(string value, string format, Type expected)
    {
        var mapper = new RuntimeTypeMapper();
        var result = mapper.Map(value, format);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Map_Default_Correct()
    {
        var mapper = new RuntimeTypeMapper();
        var result = mapper.Map("non-existing", null);
        Assert.That(result, Is.EqualTo(typeof(object)));
    }

    [Test]
    public void Register_Timespan_Correct()
    {
        var mapper = new RuntimeTypeMapper();
        var result = mapper.Map("timespan", null);
        Assert.That(result, Is.EqualTo(typeof(object)));

        mapper.Register("timespan", null, typeof(TimeSpan));
        result = mapper.Map("timespan", null);
        Assert.That(result, Is.EqualTo(typeof(TimeSpan)));

        mapper.Register("timespan", null, typeof(TimeOnly));
        result = mapper.Map("timespan", null);
        Assert.That(result, Is.EqualTo(typeof(TimeOnly)));
    }
}
