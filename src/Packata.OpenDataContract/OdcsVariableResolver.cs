using System.Text.RegularExpressions;

namespace Packata.OpenDataContract;

/// <summary>Explicitly resolves ODCS variable expressions without changing serialized contracts.</summary>
public static partial class OdcsVariableResolver
{
    public static string Resolve(string value, IReadOnlyDictionary<string, string?> variables)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(variables);

        return VariableExpression().Replace(value, match =>
        {
            var name = match.Groups["name"].Value;
            if (variables.TryGetValue(name, out var replacement) && replacement is not null)
                return replacement;

            if (match.Groups["default"].Success)
                return match.Groups["default"].Value;

            throw new KeyNotFoundException($"No value was supplied for ODCS variable '{name}'.");
        });
    }

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::-((?<default>[^}]*)))?\}")]
    private static partial Regex VariableExpression();
}
