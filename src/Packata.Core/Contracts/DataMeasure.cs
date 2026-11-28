namespace Packata.Core.Contracts;

/// <summary>A dataset-level measure declared independently of the row schema.</summary>
public sealed record DataMeasure(string Name, string? Description = null, string? ResultType = null)
{
    /// <summary>Original expressions in declaration order.</summary>
    /// <value>The unqualified expression, if declared, followed by any dialect-specific expressions.</value>
    public IReadOnlyList<DataMeasureExpression> Expressions { get; init; } = [];
}

/// <summary>Original formula text and its declared dialect, when available.</summary>
public sealed record DataMeasureExpression(string Text, string? Dialect = null);
