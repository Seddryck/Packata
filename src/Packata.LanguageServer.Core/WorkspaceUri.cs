using System.Text.RegularExpressions;

namespace Packata.LanguageServer.Core;

public static class WorkspaceUri
{
    /// <summary>VS Code percent-encodes Windows drive colons; System.Uri otherwise interprets those as Unix paths.</summary>
    public static Uri Parse(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        return uri.IsFile
            ? new Uri(Regex.Replace(uri.AbsoluteUri, @"^(file:///[a-zA-Z])%3[aA]/", "$1:/", RegexOptions.None, TimeSpan.FromSeconds(1)))
            : uri;
    }
}
