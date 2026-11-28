using Packata.Core.Contracts;
using Packata.OpenDataContract.Types;

namespace Packata.OpenDataContract.Mapping;

internal static class MeasureMapper
{
    private const string DialectProperty = "expressionDialect";

    internal static DataMeasure FromProperty(SchemaProperty property, string? resultType)
    {
        var dialect = property.CustomProperties.LastOrDefault(entry =>
            entry is { Vendor: "packata", Property: DialectProperty })?.Value as string;
        return new(property.Name, property.Description, property.LogicalType switch
        {
            TimestampLogicalType { Timezone: true } => "datetimeWithTimezone",
            TimestampLogicalType { Timezone: false } => "datetime",
            _ => resultType,
        })
        {
            Expressions = property.TransformLogic is { } logic ? [new(logic, dialect)] : [],
        };
    }

    internal static SchemaProperty ToProperty(DataMeasure measure, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var property = new SchemaProperty { Name = measure.Name, Description = measure.Description, SemanticType = "measure" };
        SetResultType(property, measure.ResultType, path, diagnostics);

        var expression = SelectExpression(measure.Expressions, path, diagnostics);
        property.TransformLogic = expression?.Text;
        if (expression?.Dialect is { } dialect)
            property.CustomProperties.Add(new() { Vendor = "packata", Property = DialectProperty, Value = dialect });
        return property;
    }

    private static void SetResultType(SchemaProperty property, string? resultType, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var type = resultType switch
        {
            "string" or "date" or "time" or "timestamp" or "number" or "integer" or "boolean" => resultType,
            "decimal" or "float" => "number",
            "datetime" or "datetimeWithTimezone" => "timestamp",
            _ => null,
        };
        Dictionary<string, object>? options = resultType switch
        {
            "datetime" => new() { ["timezone"] = false },
            "datetimeWithTimezone" => new() { ["timezone"] = true },
            _ => null,
        };
        property.SetLogicalType(type, options);
        if (resultType is not null && (type is null || resultType == "float"))
        {
            diagnostics.Add(new("ODCS011", MappingSeverity.Warning, path + "/resultType",
                $"ODCS cannot preserve the declared measure result type '{resultType}' exactly."));
        }
    }

    private static DataMeasureExpression? SelectExpression(IReadOnlyList<DataMeasureExpression> expressions,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (expressions.Count <= 1)
            return expressions.FirstOrDefault();

        var unqualified = expressions.Where(expression => expression.Dialect is null).Take(2).ToArray();
        var selected = unqualified.Length == 1 ? unqualified[0] : null;
        var message = selected is null
            ? "ODCS accepts one transformLogic. Multiple expressions were declared without a single default."
            : "ODCS accepts one transformLogic. Only the default expression was exported.";
        diagnostics.Add(new("ODCS012", MappingSeverity.Warning, path + "/expressions", message));
        return selected;
    }
}
