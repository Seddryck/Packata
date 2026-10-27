#nullable enable
using System.Text.RegularExpressions;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif;

internal static class MetricValidation
{
    internal static void Validate(DataQualityRule rule, LibraryQuality library, MetricDefinition definition, List<CompilationFinding> findings)
    {
        void Invalid(string code, string message) => findings.Add(new(rule.ExecutionId, CompilationFindingKind.Invalid, code, message));
        var field = rule.Target?.FieldPath.Count > 0;
        if (field ? !definition.SupportsField : !definition.SupportsAsset) Invalid("METRIC_SCOPE", "The metric does not support this target scope.");
        if (!definition.Units.Contains(library.Unit, StringComparer.Ordinal)) Invalid("METRIC_UNIT", "The metric does not support the requested unit.");
        ValidateArguments(library, definition, Invalid);
        ValidateGrouping(library, definition, field, Invalid);
    }

    private static void ValidateArguments(LibraryQuality library, MetricDefinition definition, Action<string, string> invalid)
    {
        foreach (var argument in definition.Arguments)
        {
            if (library.Arguments.TryGetValue(argument.Name, out var value)) ValidateArgument(argument, value, invalid);
            else if (argument.Required) invalid("MISSING_ARGUMENT", "A required metric argument is absent.");
        }
        if (library.Arguments.Keys.Any(name => definition.Arguments.All(a => a.Name != name))) invalid("UNKNOWN_ARGUMENT", "An unknown metric argument was supplied.");
        if (definition.RequiresCriterion && !library.Arguments.ContainsKey("validValues") && !library.Arguments.ContainsKey("pattern"))
            invalid("MISSING_CRITERION", "At least one validity criterion must be supplied.");
    }

    private static void ValidateArgument(MetricArgument argument, QualityValue value, Action<string, string> invalid)
    {
        if (value.Kind != argument.Kind) { invalid("ARGUMENT_TYPE", "A metric argument has the wrong type."); return; }
        if (value.Kind == QualityValueKind.List && value.Value is IReadOnlyList<QualityValue> values
            && values.Any(v => v.Kind != QualityValueKind.Scalar || (argument.StringItems && v.ToObject() is not string)))
            invalid("ARGUMENT_ITEMS", "Metric array items must be supported typed scalars (property paths must be strings).");
        if (argument.Name == "pattern") ValidatePattern(value, invalid);
    }

    private static void ValidatePattern(QualityValue value, Action<string, string> invalid)
    {
        if (value.ToObject() is not string pattern) { invalid("PATTERN_TYPE", "Pattern must be a regex string."); return; }
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { invalid("PATTERN_SYNTAX", "Pattern is not a valid .NET regular expression."); }
    }

    private static void ValidateGrouping(LibraryQuality library, MetricDefinition definition, bool field, Action<string, string> invalid)
    {
        var groups = definition.Program.Reduction == MetricReduction.CountExcessOccurrences;
        var hasProperties = library.Arguments.TryGetValue("properties", out var properties);
        if (groups && !field && (!hasProperties || properties!.Value is not IReadOnlyList<QualityValue> { Count: > 0 }))
            invalid("MISSING_PROPERTIES", "Schema-level grouping requires a non-empty properties array.");
        if (field && hasProperties) invalid("ARGUMENT_SCOPE", "Field grouping uses its target; properties is a schema-level argument.");
    }
}
