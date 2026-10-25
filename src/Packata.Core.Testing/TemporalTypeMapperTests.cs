using System.Data;
using NUnit.Framework;

namespace Packata.Core.Testing;

public class TemporalTypeMapperTests
{
    [TestCase("%Y-%m-%dT%H:%M:%S%z", true)]
    [TestCase("%Y-%m-%d %H:%M:%S%:z", true)]
    [TestCase("%Y-%m-%d %H:%M:%S%%z", false)]
    [TestCase("%Y-%m-%d %H:%M:%S%%:z", false)]
    [TestCase("%Y-%m-%d %H:%M:%S%%%z", true)]
    [TestCase("%Y-%m-%d %H:%M:%S", false)]
    [TestCase("%Y-%m-%d %H:%M:%S%Z", false)]
    [TestCase(null, false)]
    [TestCase("default", false)]
    [TestCase("any", false)]
    public void Map_datetime_uses_only_unescaped_offset_directives(string? format, bool offset)
    {
        Assert.Multiple(() =>
        {
            Assert.That(new RuntimeTypeMapper().Map("datetime", format),
                Is.EqualTo(offset ? typeof(DateTimeOffset) : typeof(DateTime)));
            Assert.That(new DbTypeMapper().Map("datetime", format),
                Is.EqualTo(offset ? DbType.DateTimeOffset : DbType.DateTime));
        });
    }

    [TestCase("date", typeof(DateOnly), DbType.Date)]
    [TestCase("time", typeof(TimeOnly), DbType.Time)]
    [TestCase("timestamp", typeof(DateTimeOffset), DbType.DateTimeOffset)]
    public void Map_other_temporal_types_are_unchanged(string type, Type runtimeType, DbType dbType)
    {
        Assert.That(new RuntimeTypeMapper().Map(type, "%z"), Is.EqualTo(runtimeType));
        Assert.That(new DbTypeMapper().Map(type, "%z"), Is.EqualTo(dbType));
    }

    [Test]
    public void Map_preserves_explicit_format_registrations()
    {
        const string format = "%Y-%m-%dT%H:%M:%S%z";
        var runtime = new RuntimeTypeMapper();
        var database = new DbTypeMapper();
        runtime.Register("datetime", format, typeof(string));
        database.Register("datetime", format, DbType.String);
        Assert.That(runtime.Map("datetime", format), Is.EqualTo(typeof(string)));
        Assert.That(database.Map("datetime", format), Is.EqualTo(DbType.String));
    }
}
