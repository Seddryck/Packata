using System.ComponentModel;
using System.Globalization;

namespace Packata.Cli;

internal sealed class PreviewLimitConverter : Int32Converter
{
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return count;
        throw new OptionConversionException("--limit must be a positive 32-bit integer.");
    }
}

internal sealed class OptionConversionException(string message) : Exception(message);
