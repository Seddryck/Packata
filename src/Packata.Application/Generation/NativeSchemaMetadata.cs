using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;
namespace Packata.Application.Generation;

/// <summary>Only declarations represented exactly by a native type may be omitted.</summary>
internal sealed record NativeSchemaMetadata(string Format, string Kind, TypeParameters Parameters, bool HasEnum)
{
    private int? IntegerWidth => Kind switch { "byte" => 8, "short" => 16, "int" or "integer" => 32, "long" => 64, _ => null };

    internal bool Represents(DataConstraint constraint) => constraint.Kind switch
    {
        "precision" when Kind == "decimal" => Equal(constraint.Value, Parameters.Precision),
        "scale" when Kind == "decimal" => Equal(constraint.Value, Parameters.Scale),
        "precision" when Parameters.TemporalPrecision == 6 => Equal(constraint.Value, 6),
        "integerWidth" => IntegerWidth is not null && Equal(constraint.Value, IntegerWidth),
        "signed" => IntegerWidth is not null && Equal(constraint.Value, true),
        _ => HasEnum && constraint.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase)
    };

    internal bool RepresentsFormat(string? format) => format?.ToLowerInvariant() switch
    {
        null => true,
        "fp32" => Kind == "float",
        "fp64" => Kind == "double",
        "i8" => IntegerWidth == 8,
        "i16" => IntegerWidth == 16,
        "i32" => IntegerWidth == 32,
        "i64" => IntegerWidth == 64,
        _ => false
    };

    internal static bool RepeatedByIntegerFormat(DataConstraint constraint, string? format)
    {
        if (format?.ToLowerInvariant() is not ("i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" or "u64")) return false;
        return constraint.Kind switch
        {
            "integerWidth" => Equal(constraint.Value, int.Parse(format[1..], System.Globalization.CultureInfo.InvariantCulture)),
            "signed" => Equal(constraint.Value, char.ToLowerInvariant(format[0]) == 'i'),
            _ => false
        };
    }

    private static bool Equal(object? value, object? native)
        => value is not null && native is not null && JToken.DeepEquals(JToken.FromObject(value), JToken.FromObject(native));
}
