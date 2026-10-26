#nullable enable
using System.Text;

namespace Packata.Rules.Sql;

/// <summary>Substitutes only identifier placeholders in SQL code, preserving quoted content and comments.</summary>
public static class SqlPlaceholderBinder
{
    public static string Bind(string query, SqlDialect dialect, IReadOnlyList<string> objectPath,
        IReadOnlyList<string>? propertyPath, Func<string, string> quoteIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return new Scanner(query, dialect, objectPath, propertyPath, quoteIdentifier).Render();
    }

    private sealed class Scanner(string query, SqlDialect dialect, IReadOnlyList<string> objects,
        IReadOnlyList<string>? properties, Func<string, string> quote)
    {
        private readonly StringBuilder _result = new(query.Length);
        private int _index;

        internal string Render()
        {
            while (_index < query.Length)
            {
                var end = ProtectedEnd(query, _index, dialect);
                if (end is { } boundary) { Copy(boundary); continue; }
                if (At(query, _index, "{object}")) Substitute(objects, "{object}".Length);
                else if (At(query, _index, "{property}")) Substitute(properties, "{property}".Length);
                else { _result.Append(query[_index]); _index++; }
            }
            return _result.ToString();
        }

        private void Copy(int end) { _result.Append(query, _index, end - _index); _index = end; }
        private void Substitute(IReadOnlyList<string>? path, int length)
        {
            if (path is null || path.Count == 0 || path.Any(s => string.IsNullOrWhiteSpace(s) || s.Contains('\0')))
                throw new ArgumentException("An SQL placeholder requires an explicit nonempty physical identifier path.");
            _result.AppendJoin('.', path.Select(quote));
            _index += length;
        }
        private static int? ProtectedEnd(string query, int index, SqlDialect dialect)
        {
            if (At(query, index, "--") || (dialect == SqlDialect.MySql && query[index] == '#'))
            {
                var end = query.IndexOf('\n', index);
                return end < 0 ? query.Length : end;
            }
            if (At(query, index, "/*")) return BlockCommentEnd(query, index);
            if (IsQuote(query[index], dialect)) return QuotedEnd(query, index, query[index], UsesBackslashEscapes(query, index, dialect));
            if (query[index] == '[' && dialect is SqlDialect.SqlServer or SqlDialect.Sqlite) return QuotedEnd(query, index, ']', false);
            return DollarQuotedEnd(query, index, dialect);
        }

        private static bool IsQuote(char character, SqlDialect dialect)
            => (character is '\'' or '"') || (character == '`' && dialect is SqlDialect.MySql or SqlDialect.Sqlite);

        private static bool UsesBackslashEscapes(string query, int index, SqlDialect dialect)
        {
            if (dialect == SqlDialect.MySql) return true;
            if (dialect is not (SqlDialect.PostgreSql or SqlDialect.DuckDb)) return false;
            if (query[index] != '\'' || index == 0 || query[index - 1] is not ('e' or 'E')) return false;
            return index < 2 || (!char.IsLetterOrDigit(query[index - 2]) && query[index - 2] != '_');
        }

        private static int? DollarQuotedEnd(string query, int index, SqlDialect dialect)
        {
            if (query[index] != '$' || dialect is not (SqlDialect.PostgreSql or SqlDialect.DuckDb)) return null;
            var delimiter = DollarDelimiter(query, index);
            if (delimiter is null) return null;
            var end = query.IndexOf(delimiter, index + delimiter.Length, StringComparison.Ordinal);
            if (end < 0) throw new ArgumentException("Unterminated SQL dollar-quoted string.");
            return end + delimiter.Length;
        }

        private static bool At(string text, int index, string token) => text.AsSpan(index).StartsWith(token, StringComparison.Ordinal);
        private static int BlockCommentEnd(string text, int start)
        {
            var depth = 1;
            var index = start + 2;
            while (index < text.Length - 1)
            {
                if (At(text, index, "/*")) { depth++; index += 2; }
                else if (At(text, index, "*/"))
                {
                    depth--;
                    index += 2;
                    if (depth == 0) return index;
                }
                else index++;
            }
            throw new ArgumentException("Unterminated SQL comment.");
        }
        private static int QuotedEnd(string text, int start, char terminator, bool backslash)
        {
            var index = start + 1;
            while (index < text.Length)
            {
                if (backslash && text[index] == '\\') { index += 2; continue; }
                if (text[index] != terminator) { index++; continue; }
                if (index + 1 < text.Length && text[index + 1] == terminator) { index += 2; continue; }
                return index + 1;
            }
            throw new ArgumentException("Unterminated SQL quoted content.");
        }
        private static string? DollarDelimiter(string text, int start)
        {
            // Positional provider parameters ($1) are not dollar-quoted strings.
            var index = start + 1;
            if (index < text.Length && text[index] == '$') return "$$";
            if (index >= text.Length || !(char.IsLetter(text[index]) || text[index] == '_')) return null;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) index++;
            return index < text.Length && text[index] == '$' ? text[start..(index + 1)] : null;
        }
    }
}
