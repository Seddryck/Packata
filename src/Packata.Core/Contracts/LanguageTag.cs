using System.Text.RegularExpressions;

namespace Packata.Core.Contracts;

/// <summary>Syntax shared by field metadata and category label selection. No OS locale lookup.</summary>
public static class LanguageTag
{
    /// <summary>Checks ordinary language tags with optional script, region, variants, extensions and private use.
    /// Registry membership and grandfathered/private-use-only tags are not supported.</summary>
    public static bool IsValid(string? value) => value is not null && Regex.IsMatch(value,
        @"\A(?:[a-z]{2,3}(?:-[a-z]{3}){0,3}|[a-z]{4}|[a-z]{5,8})(?:-[a-z]{4})?(?:-(?:[a-z]{2}|[0-9]{3}))?" +
        @"(?:-(?:[a-z0-9]{5,8}|[0-9][a-z0-9]{3}))*(?:-[0-9a-wy-z](?:-[a-z0-9]{2,8})+)*(?:-x(?:-[a-z0-9]{1,8})+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
}
