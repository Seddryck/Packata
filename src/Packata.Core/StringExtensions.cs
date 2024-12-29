using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public static class StringExtensions
{
    /// <summary>
    /// Converts a given string to PascalCase using Span<char>.
    /// </summary>
    /// <param name="input">The input string to convert.</param>
    /// <returns>The PascalCase version of the input string.</returns>
    public static string ToPascalCase(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        ReadOnlySpan<char> span = input.AsSpan();
        Span<char> buffer = stackalloc char[input.Length];
        int bufferIndex = 0;
        bool capitalizeNext = true;

        foreach (var c in span)
        {
            if (c == ' ' || c == '_' || c == '-')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                buffer[bufferIndex++] = char.ToUpperInvariant(c);
                capitalizeNext = false;
            }
            else
            {
                buffer[bufferIndex++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..bufferIndex]);
    }

    /// <summary>
    /// Converts a given string to camelCase using Span<char>.
    /// </summary>
    /// <param name="input">The input string to convert.</param>
    /// <returns>The camelCase version of the input string.</returns>
    public static string ToCamelCase(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        ReadOnlySpan<char> span = input.AsSpan();
        Span<char> buffer = stackalloc char[input.Length];
        int bufferIndex = 0;
        bool capitalizeNext = false;

        foreach (var c in span)
        {
            if (c == ' ' || c == '_' || c == '-')
            {
                capitalizeNext = true;
                continue;
            }

            if (bufferIndex == 0)
            {
                // First character: lowercase
                buffer[bufferIndex++] = char.ToLowerInvariant(c);
            }
            else if (capitalizeNext)
            {
                // Capitalize the next character
                buffer[bufferIndex++] = char.ToUpperInvariant(c);
                capitalizeNext = false;
            }
            else
            {
                // Append as lowercase
                buffer[bufferIndex++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..bufferIndex]);
    }
}
