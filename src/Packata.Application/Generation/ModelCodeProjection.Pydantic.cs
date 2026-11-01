using System.Globalization;
using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

internal sealed partial class ModelCodeProjection
{
    private static readonly HashSet<string> IntegralConstraints = new(["min_length", "max_length", "max_digits", "decimal_places"], StringComparer.Ordinal);

    private string PydanticArguments(DataField field, FieldTypeResolution resolution, string path, bool item)
    {
        if (!pydantic) return "";
        var args = new List<string>();
        if (!item && !field.Required && field.Nullable) args.Add("default=None");
        if (!item) args.Add("alias=" + Literal(field.Name));
        if (field.Description is not null) args.Add("description=" + Literal(field.Description));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constraint in field.Constraints)
            AddConstraint(args, seen, constraint, resolution, path);
        WarnValidationSemantics(resolution, path);
        return string.Join(", ", args);
    }

    private void AddConstraint(List<string> args, HashSet<string> seen, DataConstraint constraint, FieldTypeResolution resolution, string path)
    {
        var key = ConstraintKey(constraint.Kind, resolution);
        if (key is not null)
        {
            AddNumericConstraint(args, seen, key, constraint, resolution, path);
            return;
        }
        if (constraint.Kind == "enum" && resolution.Field.ValueDomain?.AllowedValues is null)
            diagnostics.Add(new("MODEL_ENUM", WarningSeverity, path, "This enum declaration has no canonical allowed-value domain and is not enforced."));
        else if (constraint.Kind is not ("required" or "enum" or "integerWidth" or "signed"))
            diagnostics.Add(new("MODEL_CONSTRAINT", WarningSeverity, path, $"Pydantic generation does not enforce '{constraint.Kind}'; it remains in canonical metadata."));
    }

    private void AddNumericConstraint(List<string> args, HashSet<string> seen, string key, DataConstraint constraint, FieldTypeResolution resolution, string path)
    {
        if (!seen.Add(key))
        {
            Error("MODEL_CONSTRAINT", path, $"Multiple constraints map to '{key}'; combine them before generation.");
            return;
        }
        var number = JToken.FromObject(constraint.Value ?? new object());
        if (!ValidConstraintNumber(number, key))
        {
            Error("MODEL_CONSTRAINT", path, $"Constraint '{constraint.Kind}' requires a numeric value.");
            return;
        }
        args.Add(key + "=" + ConstraintValue(resolution, number, constraint.Value));
    }

    private static bool ValidConstraintNumber(JToken number, string key)
    {
        if (number.Type is not (JTokenType.Integer or JTokenType.Float)) return false;
        if (!double.TryParse(number.ToString(Newtonsoft.Json.Formatting.None), CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return false;
        if (!IntegralConstraints.Contains(key)) return true;
        return number.Type == JTokenType.Integer && value >= 0;
    }

    private string ConstraintValue(FieldTypeResolution resolution, JToken number, object? value)
    {
        if (resolution.LogicalType != NumberType || IsFloatingPoint(resolution) || number.Type != JTokenType.Float)
            return PythonValue(value);
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return "Decimal(" + Literal(text) + ")";
    }

    private static string? ConstraintKey(string kind, FieldTypeResolution resolution)
        => NumericConstraint(kind, resolution) ?? LengthConstraint(kind, resolution) ?? DecimalConstraint(kind, resolution);

    private static string? NumericConstraint(string kind, FieldTypeResolution resolution)
        => resolution.LogicalType is "integer" or NumberType ? NumericConstraintKey(kind) : null;

    private static string? LengthConstraint(string kind, FieldTypeResolution resolution)
        => (resolution.LogicalType is StringType or "array" || resolution.Field.Map is not null) ? LengthConstraintKey(kind) : null;

    private static string? DecimalConstraint(string kind, FieldTypeResolution resolution)
        => resolution.LogicalType == NumberType ? DecimalConstraintKey(kind) : null;

    private static string? DecimalConstraintKey(string kind) => kind switch
    {
        "precision" => "max_digits", "scale" => "decimal_places", _ => null
    };

    private static string? NumericConstraintKey(string kind) => kind switch
    {
        "minimum" => "ge", "maximum" => "le", "exclusiveMinimum" => "gt", "exclusiveMaximum" => "lt", _ => null
    };

    private static string? LengthConstraintKey(string kind) => kind switch
    {
        "minLength" or "minItems" => "min_length", "maxLength" or "maxItems" => "max_length", _ => null
    };

    private void WarnValidationSemantics(FieldTypeResolution resolution, string path)
    {
        var field = resolution.Field;
        if (field.QualityRules.Count > 0 || field.ValueDomain?.Reference is not null || (field.Format is not null && resolution.LogicalType == StringType))
            diagnostics.Add(new("MODEL_VALIDATION", WarningSeverity, path, "Quality rules, referenced domains and string formats need separate validation."));
    }
}
