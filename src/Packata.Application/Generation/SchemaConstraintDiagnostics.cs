using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
namespace Packata.Application.Generation;

internal static class SchemaConstraintDiagnostics
{
    private static readonly HashSet<string> DescriptiveConstraints = new(StringComparer.OrdinalIgnoreCase) {
        "primaryKey", "unique", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum",
        "minLength", "maxLength", "pattern", "minItems", "maxItems", "uniqueItems", "minProperties", "maxProperties", "required"
    };

    internal static GenerationDiagnostic? Constraint(DataField field, DataConstraint constraint, string format, string path)
    {
        if (constraint.Kind.Equals("required", StringComparison.OrdinalIgnoreCase) && Equals(constraint.Value, field.Required)) return null;
        if (constraint.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase)) return Enum(field, constraint, format, path);
        if (constraint.Kind.Equals("default", StringComparison.OrdinalIgnoreCase) && format == "avro")
            return Gap(path, "Avro supports explicit defaults, but this exporter does not emit them yet. The declaration is preserved in Packata metadata; no default is inferred.", format);
        if (DescriptiveConstraints.Contains(constraint.Kind) || constraint.Kind.Equals("default", StringComparison.OrdinalIgnoreCase))
            return Limit("SCHEMA_CONSTRAINT", path, $"The target schema cannot enforce constraint '{constraint.Kind}'. Its declaration is preserved in Packata metadata.", format);
        return Gap(path, $"This exporter does not interpret constraint '{constraint.Kind}' yet. Its declaration is preserved in Packata metadata and is not enforced.", format);
    }

    private static GenerationDiagnostic? Enum(DataField field, DataConstraint constraint, string format, string path)
    {
        if (format == "spark-json") return Limit("SCHEMA_ENUM_FORMAT_LIMIT", path, "Spark StructType has no native enum type. Allowed values are preserved in Packata metadata and are not enforced by the schema.", format);
        if (AvroEnumProjection.Symbols(field) is not null) return null;
        if (RequiresEnumImplementation(field, constraint))
            return Gap(path, "This exporter does not yet generate Avro enums for empty or multiple membership declarations. The original type and declarations are preserved in Packata metadata.", format);
        return Limit("SCHEMA_ENUM_FORMAT_LIMIT", path, "Avro enums require unique string symbols matching Avro identifier syntax. This restriction cannot map directly without changing its values; the original type and membership are preserved in Packata metadata.", format);
    }

    private static bool RequiresEnumImplementation(DataField field, DataConstraint constraint)
    {
        if (!string.Equals(field.LogicalType, "string", StringComparison.OrdinalIgnoreCase)) return false;
        if (field.Constraints.Count(c => c.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase)) > 1) return true;
        return constraint.Value is not null && JToken.FromObject(constraint.Value) is JArray { Count: 0 };
    }

    internal static GenerationDiagnostic Limit(string code, string path, string message, string format)
        => new(code, "Information", path, message, format);
    private static GenerationDiagnostic Gap(string path, string message, string format)
        => new("SCHEMA_IMPLEMENTATION_GAP", "Warning", path, message, format);
}
