using System.Data;
using System.Text.RegularExpressions;

namespace Packata.Provisioners.Database;

internal sealed record PhysicalTypeDefinition(DbType DbType, int? Length = null,
    int? Precision = null, int? Scale = null)
{
    private static readonly Regex TypePattern = new(
        @"^(?<name>[a-z][a-z0-9 ]*)(?:\((?<first>\d+)(?:\s*,\s*(?<second>\d+))?\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static PhysicalTypeDefinition? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = TypePattern.Match(value.Trim());
        if (!match.Success) return null;
        var name = match.Groups["name"].Value.Trim().ToLowerInvariant();
        int? first = match.Groups["first"].Success ? int.Parse(match.Groups["first"].Value) : null;
        int? second = match.Groups["second"].Success ? int.Parse(match.Groups["second"].Value) : null;
        return name switch
        {
            "char" or "character" or "varchar" or "nvarchar" or "character varying"
                when first is > 0 => new(DbType.String, Length: first),
            "decimal" or "numeric" when first is > 0 && second is >= 0 && second <= first
                => new(DbType.Decimal, Precision: first, Scale: second),
            "decimal" or "numeric" => new(DbType.Decimal),
            "smallint" or "int2" => new(DbType.Int16),
            "int" or "integer" or "int4" => new(DbType.Int32),
            "bigint" or "int8" => new(DbType.Int64),
            "real" or "float4" => new(DbType.Single),
            "double" or "double precision" or "float8" => new(DbType.Double),
            "boolean" or "bool" or "bit" => new(DbType.Boolean),
            "date" => new(DbType.Date),
            "time" => new(DbType.Time),
            "datetime" or "timestamp" or "timestamp without time zone" => new(DbType.DateTime),
            "uuid" or "uniqueidentifier" => new(DbType.Guid),
            "binary" or "varbinary" or "bytea" => new(DbType.Binary),
            "text" or "string" => new(DbType.String),
            _ => null
        };
    }
}
