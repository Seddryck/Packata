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
        var type = measure.ResultType switch
        {
            "string" or "date" or "time" or "timestamp" or "number" or "integer" or "boolean" => measure.ResultType,
            "decimal" or "float" => "number",
            "datetime" or "datetimeWithTimezone" => "timestamp",
            _ => null,
        };
        Dictionary<string, object>? options = measure.ResultType switch
        {
            "datetime" => new() { ["timezone"] = false },
            "datetimeWithTimezone" => new() { ["timezone"] = true },
            _ => null,
        };
        property.SetLogicalType(type, options);
        if (measure.ResultType is { } resultType && (type is null || resultType == "float"))
        {
            diagnostics.Add(new("ODCS011", MappingSeverity.Warning, path + "/resultType",
                $"ODCS cannot preserve the declared measure result type '{resultType}' exactly."));
        }

        var expressions = measure.Expressions;
        var unqualified = expressions.Where(expression => expression.Dialect is null).Take(2).ToArray();
        var selected = expressions.Count == 1 ? expressions[0] : unqualified.Length == 1 ? unqualified[0] : null;
        if (expressions.Count > 1)
        {
            var message = selected is null
                ? "ODCS accepts one transformLogic. Multiple expressions were declared without a single default."
                : "ODCS accepts one transformLogic. Only the default expression was exported.";
            diagnostics.Add(new("ODCS012", MappingSeverity.Warning, path + "/expressions", message));
        }

        property.TransformLogic = selected?.Text;
        if (selected?.Dialect is { } dialect)
            property.CustomProperties.Add(new() { Vendor = "packata", Property = DialectProperty, Value = dialect });
        return property;
    }
}
