using Chrononuensis;
using Packata.Core.Contracts;

namespace Packata.ExtendedTypes;

internal static class ChrononuensisProfile
{
    internal static Type? SelectType(DataField field) => field.LogicalType?.Trim().ToLowerInvariant() switch
    {
        "year" => typeof(Year),
        "yearmonth" => typeof(YearMonth),
        _ => null
    };
}
