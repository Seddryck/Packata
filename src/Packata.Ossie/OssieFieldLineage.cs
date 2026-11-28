using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.Ossie;

internal static partial class OssieFieldLineage
{
    private const string PortableDialect = "OSSIE_SQL_2026";
    private const string Identifier = """(?:"(?:[^"]|"")+"|[A-Za-z_][A-Za-z0-9_]*)""";
    private const string Number = @"[0-9]+(?:\.[0-9]+)?";
    private const string Operand = "(?:" + Identifier + "|" + Number + ")";
    private const string Term = @"[+-]?\s*" + Operand;
    private static readonly HashSet<string> SqlConstants = new(StringComparer.OrdinalIgnoreCase)
    {
        "NULL", "TRUE", "FALSE", "UNKNOWN", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "LOCALTIME", "LOCALTIMESTAMP", "CURRENT_USER", "SESSION_USER", "SYSTEM_USER", "USER",
    };

    internal static FieldLineage Map(OssieField field, string source, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var alternatives = field.Expression.Dialects ?? [];
        var portable = alternatives.Where(alternative => alternative.Dialect == PortableDialect).Take(2).ToArray();
        var logic = field.Expression.Sql ?? (portable.Length == 1 ? portable[0].Expression : null);
        if (logic is null || logic.Contains("--", StringComparison.Ordinal) || !SourceExpression().IsMatch(logic))
        {
            diagnostics.Add(new("OSSIE003", MappingSeverity.Warning, path,
                "The source column dependencies could not be resolved from the declared field expression."));
            return new([field.Name], [new(source, null, [])],
                Logic: logic ?? (alternatives.Count == 1 ? alternatives[0].Expression : null));
        }

        var names = SourceOperand().Matches(logic).Select(match => match.Value)
            .Where(value => !char.IsAsciiDigit(value[0]) && !SqlConstants.Contains(value))
            .DistinctBy(OssieDeclarationValidation.Normalize, StringComparer.Ordinal)
            .Select(Unquote).ToArray();
        var isIdentity = names.Length == 1 && DirectColumn().IsMatch(logic);
        return new([field.Name], [new(source, null, names)], Logic: logic, IsIdentity: isIdentity);
    }

    private static string Unquote(string identifier) => identifier[0] == '"'
        ? identifier[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
        : identifier;

    // Bind unqualified names in simple arithmetic. More complex SQL and dialects stay opaque.
    [GeneratedRegex(@"\A\s*" + Term + @"(?:\s*[-+*/%]\s*" + Term + @")*\s*\z", RegexOptions.NonBacktracking)]
    private static partial Regex SourceExpression();

    [GeneratedRegex(Operand, RegexOptions.NonBacktracking)]
    private static partial Regex SourceOperand();

    [GeneratedRegex(@"\A\s*" + Identifier + @"\s*\z", RegexOptions.NonBacktracking)]
    private static partial Regex DirectColumn();
}
