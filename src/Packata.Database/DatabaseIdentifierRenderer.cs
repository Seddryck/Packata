using DubUrl.Querying.Dialects;

namespace Packata.Database;

internal static class DatabaseIdentifierRenderer
{
    public static string Render(IDialect dialect, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            throw new ArgumentException("A database identifier must be nonempty and cannot contain NUL.", nameof(value));
        // Legacy DubUrl formatters do not escape closing delimiters. Obtain the provider's
        // delimiter using a name requiring quoting, then escape before calling its renderer.
        // Its SQLite renderer leaves identifiers unquoted, so use SQLite's ANSI identifier syntax.
        if (dialect.GetType().FullName == "DubUrl.Provider.Sqlite.Sql.SqliteDialect")
            return $"\"{value.Replace("\"", "\"\"")}\"";
        var probe = dialect.Renderer.Render("Packata identifier", "identity");
        if (probe.StartsWith('[') && probe.EndsWith(']'))
            return dialect.Renderer.Render(value.Replace("]", "]]"), "identity");
        if (probe.StartsWith('"') && probe.EndsWith('"'))
            return dialect.Renderer.Render(value.Replace("\"", "\"\""), "identity");
        if (probe.StartsWith('`') && probe.EndsWith('`'))
            return dialect.Renderer.Render(value.Replace("`", "``"), "identity");
        if (value.Any(character => !char.IsLetterOrDigit(character) && character != '_') || char.IsDigit(value[0]))
            throw new DatabaseConnectionException("database-identifier-unsupported", "The provider cannot safely render this database identifier.");
        return dialect.Renderer.Render(value, "identity");
    }
}
