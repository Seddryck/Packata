namespace Packata.Core.Parsing;

/// <summary>Recognizes decimal scalar syntax without converting string codes or evaluating regular expressions.</summary>
public static class ScalarSyntax
{
    public static bool IsInteger(ReadOnlySpan<char> text)
    {
        var position = 0;
        SkipSign(text, ref position);
        return ReadDigits(text, ref position) && position == text.Length;
    }

    public static bool IsNumber(ReadOnlySpan<char> text)
    {
        var position = 0;
        SkipSign(text, ref position);
        var integralDigits = ReadDigits(text, ref position);
        if (position < text.Length && text[position] == '.')
        {
            position++;
            var fractionalDigits = ReadDigits(text, ref position);
            if (!integralDigits && !fractionalDigits) return false;
        }
        else if (!integralDigits) return false;

        if (position < text.Length && text[position] is 'e' or 'E')
        {
            position++;
            SkipSign(text, ref position);
            if (!ReadDigits(text, ref position)) return false;
        }
        return position == text.Length;
    }

    private static void SkipSign(ReadOnlySpan<char> text, ref int position)
    {
        if (position < text.Length && text[position] is '+' or '-') position++;
    }

    private static bool ReadDigits(ReadOnlySpan<char> text, ref int position)
    {
        var start = position;
        while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
        return position > start;
    }
}
