#nullable enable
using Expressif;
using Expressif.Syntax;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif;

/// <summary>A prepared, capability-limited Expressif program. Runtime bindings and frames are isolated per run.</summary>
public sealed class PreparedQualityProgram
{
    public const string BackendVersion = "expressif/2.93.1;packata/1";
    private static readonly HashSet<string> AllowedCalls = new(StringComparer.OrdinalIgnoreCase)
    {
        "neutral", "null", "is-null", "not-null", "is-not-null", "true", "false", "positive", "negative", "zero", "one",
        "equal-to", "greater-than", "greater-than-or-equal", "less-than", "less-than-or-equal", "within-interval",
        "add", "subtract", "multiply", "divide", "round", "floor", "ceiling", "absolute", "abs",
        "trim", "lower", "upper", "text-to-lower", "text-to-upper", "length", "text-to-length",
        "starts-with", "ends-with", "contains", "empty", "empty-or-null", "lower-case", "upper-case",
        "count", "sum", "min", "max", "average", "map", "filter", "first", "last", "every", "any"
    };
    private readonly IExpression? _expression;
    private readonly Predication? _predicate;
    private readonly string[] _variables;
    private readonly RecordAccessSyntax[] _fields;
    public QualityProgramKind Kind { get; }

    public PreparedQualityProgram(string program, QualityProgramKind kind)
    {
        Kind = kind;
        var syntax = ExpressionParser.Parse(program);
        var nodes = Walk(syntax).ToArray();
        if (nodes.OfType<FunctionCallSyntax>().Any(call => !AllowedCalls.Contains(call.Name)))
            throw new NotSupportedException("The program contains an operation outside the quality adapter's supported capabilities.");
        _variables = nodes.OfType<VariableSyntax>().Select(v => v.Name).Distinct(StringComparer.Ordinal).ToArray();
        _fields = nodes.OfType<RecordAccessSyntax>().ToArray();
        if (kind == QualityProgramKind.Predicate) _predicate = Predication.Create(program);
        else _expression = Expression.Create(program);
    }

    private static IEnumerable<SyntaxNode> Walk(SyntaxNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child)) yield return descendant;
    }

    public Func<object?, object?> Bind(IReadOnlyDictionary<string, QualityValue> bindings, DateTimeOffset observationTime)
    {
        var values = bindings.ToDictionary(p => p.Key, p => p.Value.ToObject(), StringComparer.Ordinal);
        if (values.ContainsKey("observationTime")) throw new ArgumentException("observationTime is reserved.");
        values["observationTime"] = observationTime;
        if (_variables.Any(name => !values.ContainsKey(name))) throw new IncompleteRuleInputException("MISSING_ARGUMENT");
        var runtime = new EvaluationContext(values);
        if (_predicate is { } predicate)
        {
            var bound = predicate.WithContext(runtime);
            return input => { ValidateFields(input); return bound.Evaluate(input); };
        }
        var expression = _expression!.WithContext(runtime);
        return input => { ValidateFields(input); return expression.Evaluate(input); };
    }

    public void ValidateFields(object? input)
    {
        // Dataset pipelines have internal scopes; their row projection validates input fields as rows are read.
        if (input is not IReadOnlyDictionary<string, object?>) return;
        foreach (var access in _fields)
        {
            object? current = input;
            foreach (var field in access.Fields)
            {
                if (!field.IsNamed || current is not IReadOnlyDictionary<string, object?> record || !record.TryGetValue(field.Name!, out current))
                    throw new IncompleteRuleInputException("MISSING_FIELD");
            }
        }
    }
}
