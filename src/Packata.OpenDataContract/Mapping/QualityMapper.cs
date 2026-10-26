using System.Text.Json;
using Packata.Core.Contracts;
using CanonicalRule = Packata.Core.Contracts.DataQualityRule;

namespace Packata.OpenDataContract.Mapping;

internal static class QualityMapper
{
    private static readonly IReadOnlyDictionary<string, ExpectationOperator> Operators = new Dictionary<string, ExpectationOperator>
    {
        ["MustBe"] = ExpectationOperator.Equal, ["MustNotBe"] = ExpectationOperator.NotEqual,
        ["MustBeGreaterThan"] = ExpectationOperator.Greater, ["MustBeGreaterOrEqualTo"] = ExpectationOperator.GreaterOrEqual,
        ["MustBeLessThan"] = ExpectationOperator.Less, ["MustBeLessOrEqualTo"] = ExpectationOperator.LessOrEqual,
        ["MustBeBetween"] = ExpectationOperator.Between, ["MustNotBeBetween"] = ExpectationOperator.NotBetween
    };

    internal static IReadOnlyList<CanonicalRule> Map(IReadOnlyList<DataQualityRule> rules, QualityTarget target,
        string path, ICollection<MappingDiagnostic> diagnostics)
        => Array.AsReadOnly(rules.Select((rule, index) => Map(rule, target, $"{path}/{index}", diagnostics)).ToArray());

    private static CanonicalRule Map(DataQualityRule native, QualityTarget target, string path,
        ICollection<MappingDiagnostic> diagnostics)
    {
        var findings = new List<MappingDiagnostic>();
        void Warn(string message) => findings.Add(new("ODCS_QUALITY", MappingSeverity.Warning, path, message));
        var metric = ResolveMetric(native, Warn);
        var type = native.Type ?? (metric is not null ? "library" : "text");
        var definition = MapDefinition(native, type, metric, Warn);
        ReportDefinition(definition, Warn);
        var declared = DeclaredExpectations(native);
        var expectations = MapExpectations(declared, Warn);
        AppendDiagnostics(findings, diagnostics);
        return new CanonicalRule(type, native.Description, native.Query)
        {
            SourceId = native.Id, SourcePath = path, Target = target, Definition = definition,
            Expectations = expectations, Metadata = MapMetadata(native),
            Extensions = ExtensionMetadata.For("odcs", MetadataSnapshot.CopyDictionary(new Dictionary<string, object?>
            {
                ["declaration"] = native, ["expectations"] = declared
            })),
            Diagnostics = Array.AsReadOnly(findings.ToArray())
        };
    }

    private static void AppendDiagnostics(IEnumerable<MappingDiagnostic> findings, ICollection<MappingDiagnostic> diagnostics)
    {
        foreach (var finding in findings) diagnostics.Add(finding);
    }

    private static string? ResolveMetric(DataQualityRule native, Action<string> warn)
    {
#pragma warning disable CS0618 // Preserve deprecated source data, preferring metric.
        if (native.Rule is not null)
        {
            var conflict = native.Metric is not null && native.Metric != native.Rule;
            warn(conflict ? "Conflicting metric and deprecated rule: metric takes precedence; both are retained in extensions."
                : "Deprecated rule mapped as metric; use metric in new documents.");
        }
        return native.Metric ?? native.Rule;
#pragma warning restore CS0618
    }

    private static IQualityDefinition MapDefinition(DataQualityRule native, string type, string? metric, Action<string> warn)
    {
        try
        {
            return type.ToLowerInvariant() switch
            {
                "library" => new LibraryQuality("odcs", "3.2.0", metric ?? "", native.Arguments.ToDictionary(p => p.Key, p => QualityValue.From(p.Value)), native.Unit ?? "rows"),
                "sql" => new SqlQuality(native.Query ?? "", native.Unit),
                "text" => new TextQuality(native.Description),
                "custom" when string.Equals(native.Engine, "expressif", StringComparison.OrdinalIgnoreCase) => ParseExpressif(native.Implementation),
                _ => new CustomQuality(native.Engine ?? native.Type, native.Implementation)
            };
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            warn("Invalid or unsupported quality definition; the original declaration is retained in extensions.");
            return new CustomQuality(native.Engine ?? type, native.Implementation);
        }
    }

    private static void ReportDefinition(IQualityDefinition definition, Action<string> warn)
    {
        if (definition is CustomQuality) warn("No execution adapter is implied for this custom engine or payload.");
        if (definition is LibraryQuality { Metric.Length: 0 } or SqlQuality { Query.Length: 0 }) warn("Required metric or SQL query is absent.");
    }

    private static Dictionary<string, object?> DeclaredExpectations(DataQualityRule native)
    {
        var declared = native.DeclaredExpectations.ToDictionary(p => p.Key, p => p.Value);
        // A caller may append to the native mutable range lists without using a setter.
        if (native.MustBeBetween.Count > 0) declared[nameof(native.MustBeBetween)] = native.MustBeBetween;
        if (native.MustNotBeBetween.Count > 0) declared[nameof(native.MustNotBeBetween)] = native.MustNotBeBetween;
        return declared;
    }

    private static IReadOnlyList<QualityExpectation> MapExpectations(Dictionary<string, object?> declared, Action<string> warn)
    {
        var expectations = new List<QualityExpectation>();
        foreach (var (name, value) in declared)
        {
            try { expectations.Add(MapExpectation(name, value)); }
            catch (ArgumentException)
            {
                warn($"Invalid expectation {name}; original operands retained. This declaration must not execute without correction.");
            }
        }
        return Array.AsReadOnly(expectations.ToArray());
    }

    private static QualityExpectation MapExpectation(string name, object? value)
    {
        if (!Operators.TryGetValue(name, out var op)) throw new ArgumentException("Unknown expectation.");
        var operand = QualityValue.From(value);
        if (op is not (ExpectationOperator.Between or ExpectationOperator.NotBetween)) return new(op, operand);
        if (operand.Value is not IReadOnlyList<QualityValue> { Count: 2 } range)
            throw new ArgumentException("Range expectations require exactly two operands.");
        return new(op, range[0], range[1]);
    }

    private static QualityMetadata MapMetadata(DataQualityRule native) => new(native.Name, native.Description, native.Dimension,
        native.BusinessImpact, native.Method, native.Severity, native.Scheduler, native.Schedule, native.Tags,
        native.AuthoritativeDefinitions.Where(r => r.Url is not null).Select(r => new AuthoritativeReference(r.Url!, r.Type)).ToArray());

    private static ExpressifQuality ParseExpressif(string? payload)
    {
        using var document = JsonDocument.Parse(payload ?? "");
        var root = document.RootElement;
        if (root.GetProperty("version").GetString() != "1") throw new ArgumentException("Unsupported payload version.");
        var kind = root.GetProperty("kind").GetString() switch
        {
            "expression" => QualityProgramKind.Expression,
            "predicate" => QualityProgramKind.Predicate,
            _ => throw new ArgumentException("Explicit program kind required.")
        };
        var scope = root.GetProperty("scope").GetString() switch
        {
            "value" => QualityInputScope.Value,
            "row" => QualityInputScope.Row,
            "dataset" => QualityInputScope.Dataset,
            "observation" => QualityInputScope.Observation,
            _ => throw new ArgumentException("Explicit scope required.")
        };
        return new(root.GetProperty("program").GetString() ?? throw new ArgumentException("Program required."), kind, scope, OriginalPayload: payload);
    }
}
