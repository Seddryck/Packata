using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Core.Storage;
using Packata.ResourceReaders.Ndjson;

namespace Packata.ResourceReaders.Testing;

public class TemporalReaderTests
{
    [TestCase("%d/%m/%Y %H:%M:%S%z", "25/10/2026 10:30:00+0530", 330)]
    [TestCase("%d/%m/%Y %H:%M:%S%z", "25/10/2026 10:30:00-0430", -270)]
    [TestCase("%d/%m/%Y %H:%M:%S%:z", "25/10/2026 10:30:00+05:30", 330)]
    [TestCase("%d/%m/%Y %H:%M:%S%:z", "25/10/2026 10:30:00-04:30", -270)]
    [TestCase("%d/%m/%YT%H:%M:%S%z", "25/10/2026T10:30:00+0000", 0)]
    public async Task OpenAsync_preserves_declared_offset(string format, string value, int minutes)
    {
        using var reader = await Open("csv", "datetime", format, value);
        Assert.That(reader.Read(), Is.True);
        var actual = reader.GetValue(0);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(DateTimeOffset)));
            Assert.That(actual, Is.TypeOf<DateTimeOffset>());
            Assert.That(((DateTimeOffset)actual).DateTime, Is.EqualTo(new DateTime(2026, 10, 25, 10, 30, 0)));
            Assert.That(((DateTimeOffset)actual).Offset, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
        });
    }

    [TestCase("csv")]
    [TestCase("ndjson")]
    public async Task OpenAsync_applies_temporal_patterns_to_indexed_and_named_schemas(string format)
    {
        using var reader = await Open(format, "datetime", "%d/%m/%Y %H:%M:%S%:z", "25/10/2026 10:30:00+05:30");
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(new DateTimeOffset(2026, 10, 25, 10, 30, 0, TimeSpan.FromMinutes(330))));
    }

    [TestCase("%d/%m/%Y %H:%M:%S", "25/10/2026 10:30:00")]
    [TestCase("%d/%m/%Y %H:%M:%S%%z", "25/10/2026 10:30:00%z")]
    [TestCase("%d/%m/%Y %H:%M:%S%%:z", "25/10/2026 10:30:00%:z")]
    [TestCase("default", "2026-10-25T10:30:00")]
    [TestCase("DEFAULT", "2026-10-25T10:30:00")]
    [TestCase(null, "2026-10-25T10:30:00")]
    public async Task OpenAsync_keeps_offset_free_values_as_datetime(string? format, string value)
    {
        using var reader = await Open("csv", "datetime", format, value);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(DateTime)));
        Assert.That(reader.GetValue(0), Is.EqualTo(new DateTime(2026, 10, 25, 10, 30, 0)));
    }

    [TestCase("date", "%d/%m/%Y", "25/10/2026")]
    [TestCase("date", "default", "2026-10-25")]
    [TestCase("time", "%Hh%Mm%Ss", "10h30m00s")]
    [TestCase("time", "default", "10:30:00")]
    [TestCase("time", null, "10:30:00")]
    public async Task OpenAsync_restores_date_and_time_formats(string type, string? format, string value)
    {
        using var reader = await Open("csv", type, format, value);
        Assert.That(reader.Read(), Is.True);
        object expected = type == "date" ? new DateOnly(2026, 10, 25) : new TimeOnly(10, 30, 0);
        Assert.That(reader.GetValue(0), Is.EqualTo(expected));
    }

    [Test]
    public async Task OpenAsync_rejects_missing_declared_offsets()
    {
        using var reader = await Open("csv", "datetime", "%d/%m/%Y %H:%M:%S%z", "25/10/2026 10:30:00");
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.Exception);
    }

    private static async Task<System.Data.IDataReader> Open(string format, string type, string? pattern, string value)
    {
        var content = format == "csv" ? $"value\r\n{value}\r\n" : $"{{\"value\":\"{value}\"}}\n";
        var factory = ResourceReaderFactory.Create(options => options.AddNdjson(), new Resolver(content));
        var endpoint = new DataEndpoint("data", "data", EndpointKind.File, null,
            new PathLocation([$"data.{format}"]), new DataFormat(format));
        return await factory.OpenAsync(endpoint, new DataSchema([new DataField("value", type, Format: pattern)]));
    }

    private sealed class Resolver(string content) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
    }
}
