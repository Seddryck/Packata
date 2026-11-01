using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

internal sealed partial class ModelCodeProjection
{
    private string PythonType(FieldTypeResolution resolution, string path)
    {
        if (resolution.Parameters.TemporalPrecision > 6)
            Error("MODEL_TEMPORAL_PRECISION", path, "Python datetime/time support at most six fractional second digits.");
        if (resolution.Parameters.TemporalSemantics is not null)
            diagnostics.Add(new("MODEL_TIMEZONE", WarningSeverity, path, "Timezone/offset semantics remain metadata and are not enforced by this model."));
        var scalar = PythonScalarType(resolution);
        if (scalar is null) return UnsupportedType(path);
        if (!pydantic) return scalar;
        scalar = EnumExpression(resolution.Field, scalar, path);
        return IntegerDomain(resolution, scalar);
    }

    private static string? PythonScalarType(FieldTypeResolution resolution) => resolution.LogicalType switch
    {
        StringType => "str",
        "integer" or "year" => "int",
        "yearmonth" => "str",
        "boolean" => "bool",
        NumberType => IsFloatingPoint(resolution) ? "float" : "Decimal",
        "date" => "date",
        "time" => "time",
        DateTimeType or "timestamp" => DateTimeType,
        ObjectType => "dict[str, object]",
        _ => null
    };

    private string EnumExpression(DataField field, string scalar, string path)
    {
        var values = field.ValueDomain?.AllowedValues;
        if (values is null) return scalar;
        if (values.Count == 0)
        {
            Error("MODEL_ENUM", path, "An empty enum has no representable model values.");
            return scalar;
        }
        if (values.All(v => v.Value is string or bool or byte or short or int or long))
            return "Literal[" + string.Join(", ", values.Select(v => PythonValue(v.Value))) + "]";
        Error("MODEL_ENUM", path, "Only string, integer and boolean enum literals are supported.");
        return scalar;
    }

    private static string IntegerDomain(FieldTypeResolution resolution, string scalar)
    {
        if (resolution.LogicalType != "integer") return scalar;
        if (resolution.Parameters.IntegerWidth is not { } width || resolution.Parameters.Signed is not { } signed) return scalar;
        var min = signed ? -(System.Numerics.BigInteger.One << (width - 1)) : System.Numerics.BigInteger.Zero;
        var max = (System.Numerics.BigInteger.One << (signed ? width - 1 : width)) - 1;
        return $"Annotated[{scalar}, Field(ge={min}, le={max})]";
    }

    private static bool IsFloatingPoint(FieldTypeResolution resolution) => resolution.SourceFormat is "fp32" or "fp64";
}
