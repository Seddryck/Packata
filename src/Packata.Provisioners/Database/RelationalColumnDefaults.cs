using System.Data;
using System.Globalization;
using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using DubUrl.Schema.Builders;
using Packata.Core.Contracts;
using DefaultExpression = Packata.Core.Contracts.ColumnDefaultExpression;
using DefaultKind = Packata.Core.Contracts.ColumnDefaultKind;
using DubUrlExpression = DubUrl.Schema.ColumnDefaultExpression;

namespace Packata.Provisioners.Database;

internal static class RelationalColumnDefaults
{
    internal static void ApplyLiteral(IDialect dialect, IColumnConstraintBuilder builder, ColumnDefaultLiteral literal,
        PhysicalTypeDefinition? storage)
    {
        var column = builder.Build();
        if (!Matches(literal.Type, column.Type))
            throw new ArgumentException($"Literal type '{literal.Type}' is incompatible with mapped column type '{column.Type}'.");

        if (!IsLiteralValue(literal))
            throw new ArgumentException($"Literal type '{literal.Type}' requires its corresponding CLR value.");
        if (column.NativeType is not null && NativeTypeRegistry.Default.TryResolve(dialect, column.NativeType, out _)
            && !MatchesNativeStorage(literal.Type, storage?.NativeType.Name))
            throw new ArgumentException($"Literal type '{literal.Type}' is incompatible with SQL type '{column.NativeType.Name}'.");

        if (literal.Value is decimal number)
        {
            if (column is NumericColumn numeric)
                ValidateDecimal(number, numeric.Precision, numeric.Scale);
            else if (storage?.NativeType.Name is "decimal" or "numeric" or "number" or "dec"
                && (storage.Precision ?? storage.Length) is int precision)
                ValidateDecimal(number, precision, storage.Scale ?? 0);
            else if (dialect is DubUrl.Provider.SqlServer.Sql.TSqlDialect)
                ValidateDecimal(number, 18, 0);
            else if (dialect is DubUrl.Provider.MySql.Sql.MySqlDialect or DubUrl.Provider.MySql.Sql.SingleStoreDialect)
                ValidateDecimal(number, 10, 0);
        }
        if (literal.Value is string value)
            ValidateString(dialect, value, storage);

        var sql = dialect.Renderer.Render(literal.Value, "value");
        ValidateTemporalLiteral(dialect, literal.Value, sql, storage);
        builder.WithDefaultValue(literal.Value);
    }

    internal static void ApplyExpression(IColumnConstraintBuilder builder, DefaultExpression expression,
        PhysicalTypeDefinition? storage)
    {
        var column = builder.Build();
        var precision = expression.FractionalSecondsPrecision;
        var definition = expression.Kind switch
        {
            DefaultKind.CurrentTimestamp when IsTimestamp(column.Type) => DubUrlExpression.CurrentTimestamp(precision),
            DefaultKind.CurrentUtcTimestamp when IsTimestamp(column.Type) => DubUrlExpression.CurrentUtcTimestamp(precision),
            DefaultKind.CurrentDate when column.Type == DbType.Date && precision is null => DubUrlExpression.CurrentDate,
            DefaultKind.CurrentTime when column.Type == DbType.Time => DubUrlExpression.CurrentTime(precision),
            DefaultKind.NextSequenceValue when precision is null && IsSequenceDestination(column, storage)
                => DubUrlExpression.NextSequenceValue(SequenceName(expression.Sequence)),
            DefaultKind.CurrentUser or DefaultKind.SessionUser or DefaultKind.NewUuid when precision is null
                => TextExpression(column, expression.Kind, storage),
            _ => throw new ArgumentException(
                $"Mapped column type '{column.Type}' or its facets are incompatible with '{expression.Kind}'.")
        };
        builder.WithDefaultExpression(definition);
    }

    internal static bool RequiresTextLength(DefaultKind? kind)
        => kind is DefaultKind.CurrentUser or DefaultKind.SessionUser or DefaultKind.NewUuid;

    internal static bool IsFixedText(string? type) => type is "char" or "nchar" or "character";

    private static DubUrlExpression TextExpression(Column column, DefaultKind kind, PhysicalTypeDefinition? storage)
    {
        if (!Matches(DbType.String, column.Type) || storage?.NativeType.Name is not
            ("char" or "nchar" or "character" or "varchar" or "nvarchar" or "character varying"
            or "varchar2" or "nvarchar2" or "text" or "string"))
            throw new ArgumentException($"{kind} requires a text column.");

        if (storage.Length is not > 0)
            throw new ArgumentException($"{kind} requires an explicit positive SQL text length. Declare a physical length or maxLength.");

        if (kind == DefaultKind.NewUuid)
        {
            var fixedWidth = IsFixedText(storage.NativeType.Name);
            if (fixedWidth ? storage.Length != 36 : storage.Length < 36)
                throw new ArgumentException("new-uuid requires exactly 36 characters for fixed-width text or at least 36 for variable-width text.");
            return DubUrlExpression.NewUuid(UuidRepresentation.CanonicalText);
        }

        return kind == DefaultKind.CurrentUser ? DubUrlExpression.CurrentUser : DubUrlExpression.SessionUser;
    }

    private static DatabaseObjectName SequenceName(ColumnDefaultSequence? sequence)
        => sequence is null ? throw new ArgumentException("Supply a qualified sequence name.")
            : new(sequence.Name, sequence.Schema, sequence.Database, sequence.Catalog);

    private static bool IsTimestamp(DbType type) => type is DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset;

    private static bool IsSequenceDestination(Column column, PhysicalTypeDefinition? storage)
    {
        if (column.Type is DbType.Int16 or DbType.Int32 or DbType.Int64)
        {
            return storage?.NativeType.Name is "smallint" or "integer" or "int" or "bigint" or "int2" or "int4" or "int8"
                or "number" or "decimal" or "numeric";
        }
        if (column.Type != DbType.Decimal || storage?.NativeType.Name is not ("numeric" or "decimal" or "dec" or "number"))
            return false;
        if (column is NumericColumn numeric)
            return numeric.Scale >= 0 && numeric.Scale < numeric.Precision;
        var precision = storage.Precision ?? storage.Length;
        return precision is null || ((storage.Scale ?? 0) >= 0 && (storage.Scale ?? 0) < precision);
    }

    private static bool IsLiteralValue(ColumnDefaultLiteral literal) => (literal.Type, literal.Value) switch
    {
        (DbType.String, string) or (DbType.Boolean, bool) or (DbType.Int16, short) or (DbType.Int32, int)
            or (DbType.Int64, long) or (DbType.Decimal, decimal) or (DbType.Date, DateOnly)
            or (DbType.Time, TimeOnly) or (DbType.DateTime, DateTime) or (DbType.DateTimeOffset, DateTimeOffset) => true,
        (DbType.Single, float value) => float.IsFinite(value),
        (DbType.Double, double value) => double.IsFinite(value),
        _ => false
    };

    private static bool MatchesNativeStorage(DbType literal, string? storage) => literal switch
    {
        DbType.String => storage is "char" or "nchar" or "character" or "varchar" or "varchar2" or "nvarchar"
            or "nvarchar2" or "character varying" or "text" or "string" or "clob" or "nclob" or "citext"
            or "tinytext" or "mediumtext" or "longtext",
        DbType.Boolean => storage is "bool" or "boolean" or "bit",
        DbType.Int16 => storage is "smallint" or "int2" or "integer" or "int" or "int4" or "bigint" or "int8",
        DbType.Int32 => storage is "integer" or "int" or "int4" or "bigint" or "int8",
        DbType.Int64 => storage is "bigint" or "int8",
        DbType.Decimal => storage is "decimal" or "numeric" or "number" or "dec",
        DbType.Single => storage is "real" or "float" or "float4" or "double" or "float8" or "double precision",
        DbType.Double => storage is "double" or "float8" or "double precision" or "float",
        DbType.Date => storage is "date",
        DbType.Time => storage is "time" or "time without time zone",
        DbType.DateTime => storage is "datetime" or "datetime2" or "smalldatetime" or "timestamp" or "timestamp without time zone",
        DbType.DateTimeOffset => storage is "datetimeoffset" or "timestamptz" or "timestamp with time zone" or "timestamp with timezone",
        _ => false
    };

    private static bool Matches(DbType literal, DbType column) => literal == column
        || (literal == DbType.String && column is DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength)
        || (literal == DbType.DateTime && column == DbType.DateTime2);

    private static void ValidateString(IDialect dialect, string value, PhysicalTypeDefinition? storage)
    {
        if (value.Contains('\0') || (dialect is DubUrl.Provider.MySql.Sql.MySqlDialect or DubUrl.Provider.MySql.Sql.SingleStoreDialect
            && value.Contains('\\')))
            throw new ArgumentException("The dialect cannot safely render this string literal.");
        if (storage is { Length: > 0 } && value.Length > storage.Length)
            throw new ArgumentException($"The literal exceeds the column length of {storage.Length}.");
        if (storage?.NativeType.Name is "char" or "nchar" or "character" && value.Length != (storage.Length ?? 1))
            throw new ArgumentException("The fixed-width destination would pad the string literal.");
        if (dialect is DubUrl.Provider.Oracle.Sql.OracleDialect && value.Length == 0)
            throw new ArgumentException("Oracle stores an empty string as NULL.");
    }

    private static void ValidateDecimal(decimal value, int precision, int scale)
    {
        var digits = value.ToString("0.############################", CultureInfo.InvariantCulture).TrimStart('-').Split('.');
        var integralDigits = digits[0].TrimStart('0').Length;
        var fractionalDigits = digits.Length == 2 ? digits[1].Length : 0;
        if (integralDigits > precision - scale || fractionalDigits > scale)
            throw new ArgumentException($"The decimal literal does not fit precision {precision} and scale {scale}.");
    }

    private static void ValidateTemporalLiteral(IDialect dialect, object value, string sql, PhysicalTypeDefinition? storage)
    {
        var invariant = CultureInfo.InvariantCulture;
        var (text, ticks) = value switch
        {
            DateOnly date => (date.ToString("yyyy-MM-dd", invariant), 0L),
            TimeOnly time => (time.ToString("HH:mm:ss.FFFFFF", invariant), time.Ticks),
            DateTime stamp => (stamp.ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", invariant), stamp.Ticks),
            DateTimeOffset stamp => (stamp.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFzzz", invariant), stamp.Ticks),
            _ => (null, 0L)
        };
        if (text is null) return;
        if (dialect is DubUrl.Provider.Sqlite.Sql.SqliteDialect && !sql.StartsWith('(') && !sql.StartsWith('\''))
            throw new ArgumentException("The SQLite temporal formatter does not produce a valid DEFAULT literal.");
        if (value is DateTimeOffset && dialect is DubUrl.Provider.MySql.Sql.MySqlDialect or DubUrl.Provider.MySql.Sql.SingleStoreDialect)
            throw new ArgumentException("The destination does not retain a timestamp offset.");
        if (!sql.Replace('T', ' ').Contains(text, StringComparison.Ordinal))
            throw new ArgumentException($"Dialect '{dialect.GetType().Name}' cannot render this temporal literal faithfully.");

        var type = storage?.NativeType.Name;
        var precision = type is "datetime" && dialect is DubUrl.Provider.SqlServer.Sql.TSqlDialect ? 0
            : type is "smalldatetime" or "date" ? 0
            : storage?.Length is >= 0 and <= 6 ? storage.Length.Value
            : dialect is DubUrl.Provider.MySql.Sql.MySqlDialect or DubUrl.Provider.MySql.Sql.SingleStoreDialect ? 0 : 6;
        var ticksPerUnit = (long)Math.Pow(10, 7 - precision);
        if (ticks % ticksPerUnit != 0 || (type == "smalldatetime" && ticks % TimeSpan.TicksPerMinute != 0))
            throw new ArgumentException($"The temporal literal exceeds the precision of SQL type '{type}'.");
    }
}
