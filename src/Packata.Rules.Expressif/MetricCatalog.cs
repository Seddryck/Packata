#nullable enable
using System.Collections;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Expressif;
using Expressif.Predicates;
using Packata.Core.Contracts;

namespace Packata.Rules.Expressif;

public enum MetricReduction { CountSelected, CountExcessOccurrences }
public sealed record MetricArgument(string Name, QualityValueKind Kind, bool Required = false, bool StringItems = false);
public sealed record ExpressifMetricProgram(string Projection, Func<IReadOnlyDictionary<string, QualityValue>, IPredicate> Selection,
    MetricReduction Reduction = MetricReduction.CountSelected, string RowsFinalization = "@numerator",
    string PercentFinalization = "@numerator | multiply(100) | divide(@denominator)");
public sealed record MetricDefinition(string Catalog, string Version, string Metric, string ProgramVersion,
    bool SupportsAsset, bool SupportsField, IReadOnlyList<MetricArgument> Arguments, IReadOnlyList<string> Units,
    ExpressifMetricProgram Program, ScalarKind ResultType = ScalarKind.Number, bool RequiresCriterion = false);

public interface IMetricCatalog
{
    MetricDefinition? Resolve(string catalog, string version, string metric);
}

/// <summary>ODCS 3.2 declarations with reproducible Packata program semantics, version 1.</summary>
public sealed class OdcsMetricCatalog : IMetricCatalog
{
    private const string IdentityProjection = "neutral";
    internal const string PatternArgument = "pattern";
    private static readonly IReadOnlyDictionary<string, MetricDefinition> Entries = Create();
    public MetricDefinition? Resolve(string catalog, string version, string metric)
        => catalog == "odcs" && version == "3.2.0" ? Entries.GetValueOrDefault(metric) : null;

    private static IReadOnlyDictionary<string, MetricDefinition> Create()
    {
        MetricDefinition Definition(string name, bool asset, bool field, MetricArgument[] arguments,
            ExpressifMetricProgram program, string[]? units = null, bool criterion = false)
            => new("odcs", "3.2.0", name, "packata-odcs/1", asset, field,
                Array.AsReadOnly(arguments), Array.AsReadOnly(units ?? ["rows", "percent"]), program, RequiresCriterion: criterion);
        var definitions = new[]
        {
            Definition("rowCount", true, false, [], new("@selected", _ => Predication.Create("true")), ["rows"]),
            Definition("nullValues", false, true, [], new(IdentityProjection, _ => Predication.Create("null"))),
            Definition("missingValues", false, true, [new("missingValues", QualityValueKind.List, true)],
                new(IdentityProjection, args => MetricPredicates.Builder(args).Create<TypedMembership>(ctx => ctx.Variables["missingValues"]).Build())),
            Definition("invalidValues", false, true, [new("validValues", QualityValueKind.List), new(PatternArgument, QualityValueKind.Scalar)],
                new(IdentityProjection, InvalidSelection), criterion: true),
            Definition("duplicateValues", true, true, [new("properties", QualityValueKind.List, StringItems: true)],
                new(IdentityProjection, _ => Predication.Create("true"), MetricReduction.CountExcessOccurrences))
        };
        return new ReadOnlyDictionary<string, MetricDefinition>(definitions.ToDictionary(d => d.Metric, StringComparer.Ordinal));
    }

    private static IPredicate InvalidSelection(IReadOnlyDictionary<string, QualityValue> arguments)
    {
        // Validity requires every supplied criterion. Invalidity is the OR of their negations.
        if (arguments.ContainsKey("validValues"))
        {
            var program = MetricPredicates.Builder(arguments).Not<TypedMembership>(ctx => ctx.Variables["validValues"]);
            if (arguments.ContainsKey(PatternArgument)) program = program.Or(MetricPredicates.Builder(arguments).Not<BoundedPattern>(ctx => ctx.Variables[PatternArgument]));
            return program.Build();
        }
        return MetricPredicates.Builder(arguments).Not<BoundedPattern>(ctx => ctx.Variables[PatternArgument]).Build();
    }
}

/// <summary>Generic Expressif extension: typed scalar membership aligned with canonical value domains.</summary>
public sealed class TypedMembership : IPredicate
{
    private readonly HashSet<ScalarValue> _values;
    public TypedMembership(Func<object?> values)
    {
        var supplied = values();
        if (supplied is not IEnumerable sequence || supplied is string) throw new ArgumentException("Membership requires an array.");
        _values = sequence.Cast<object?>().Select(ScalarValue.From).ToHashSet();
    }
    object? global::Expressif.Functions.IFunction.Evaluate(object? value) => Evaluate(value);
    public bool Evaluate(object? value) => ScalarValue.TryCreate(value, out var scalar)
        ? _values.Contains(scalar) : throw new ArgumentException("Membership input must be scalar.");
}

/// <summary>Generic Expressif extension: ordinal .NET regular expressions with an explicit match timeout.</summary>
public sealed class BoundedPattern : IPredicate
{
    object? global::Expressif.Functions.IFunction.Evaluate(object? value) => Evaluate(value);
    private readonly Regex _pattern;
    public BoundedPattern(Func<string> pattern) => _pattern = new(pattern(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public bool Evaluate(object? value) => value is string text && _pattern.IsMatch(text);
}

internal static class MetricPredicates
{
    internal static PredicationBuilder Builder(IReadOnlyDictionary<string, QualityValue> bindings) => new(new Context(bindings.ToDictionary(p => p.Key, p => p.Value.ToObject())),
        new MetricPredicationFactory(), new global::Expressif.Serializers.PredicationSerializer());

    private sealed class MetricPredicationFactory : PredicationFactory
    {
        public MetricPredicationFactory() : base(new MetricTypeMapper(), new global::Expressif.Predicates.Operators.UnaryOperatorFactory(),
            new global::Expressif.Predicates.Operators.BinaryOperatorFactory()) { }
    }

    private sealed class MetricTypeMapper : PredicateTypeMapper
    {
        protected override IDictionary<string, Type> Initialize()
        {
            var mapping = base.Initialize();
            mapping["typedmembership"] = typeof(TypedMembership);
            mapping["typed-membership"] = typeof(TypedMembership);
            mapping["boundedpattern"] = typeof(BoundedPattern);
            mapping["bounded-pattern"] = typeof(BoundedPattern);
            return mapping;
        }
    }
}
