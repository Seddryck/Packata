using System.Globalization;
using NUnit.Framework;

namespace Packata.Core.Testing;

public class DateTimeFormatConverterTests
{
    [TestCase("%Y-%m-%dT%H:%M:%S", "2026-10-25T10:30:00")]
    [TestCase("%d/%m/%Y %H:%M:%S %%z", "25/10/2026 10:30:00 %z")]
    [TestCase("%d/%m/%Y %H:%M:%S %%:z", "25/10/2026 10:30:00 %:z")]
    [TestCase("%Y-%m-%dT%H:%M:%S.%f", "2026-10-25T10:30:00.123456")]
    [TestCase("%Y-%m-%d 'at' %H:%M:%S", "2026-10-25 'at' 10:30:00")]
    public void Convert_preserves_directives_and_literal_text(string pattern, string text)
    {
        var converted = DateTimeFormatConverter.Convert(pattern);
        var parsed = DateTime.ParseExact(text, converted, CultureInfo.InvariantCulture);
        Assert.That(parsed.ToString(converted, CultureInfo.InvariantCulture), Is.EqualTo(text));
    }

    [TestCase("%Z")]
    [TestCase("%j")]
    public void Convert_rejects_directives_without_a_supported_dotnet_equivalent(string pattern)
    {
        Assert.That(() => DateTimeFormatConverter.Convert(pattern), Throws.TypeOf<NotSupportedException>());
    }
}
