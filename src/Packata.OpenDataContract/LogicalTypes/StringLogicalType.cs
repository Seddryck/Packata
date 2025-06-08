using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;

namespace Packata.OpenDataContract.Types;

/// <summary>
/// Additional metadata options for a logical type "string".
/// </summary>
public class StringLogicalType : ILogicalType
{
    public StringLogicalType(Dictionary<string, object>? dict)
    {
        if (dict is null)
            return;

        Format = dict.GetValueOrDefault("format") as string;
        MaxLength = ConvertInt(dict.GetValueOrDefault("maxLength"));
        MinLength = ConvertInt(dict.GetValueOrDefault("minLength"));
        Pattern = dict.GetValueOrDefault("pattern") as string;
    }

    private static int? ConvertInt(object? value)
        => value is string str && !string.IsNullOrEmpty(str) ? int.TryParse(str, out var result) ? result : null : null;

    /// <summary>
    /// Provides extra context about what format the string follows.
    /// For example: password, byte, binary, email, uuid, uri, hostname, ipv4, ipv6.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Maximum length of the string.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Minimum length of the string.
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Regular expression pattern to define valid values.
    /// Follows the regular expression syntax from ECMA-262 (https://262.ecma-international.org/5.1/#sec-15.10.1).
    /// </summary>
    public string? Pattern { get; set; }
}
