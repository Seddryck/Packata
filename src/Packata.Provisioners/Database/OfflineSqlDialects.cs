using DubUrl.Querying.Dialects;
namespace Packata.Provisioners.Database;
public sealed record OfflineSqlDialect(string Id, string Label);
/// <summary>Bundled SQL metadata only. Registry construction never creates connection/provider sessions.</summary>
public static class OfflineSqlDialects
{
    public static IReadOnlyList<OfflineSqlDialect> Catalog { get; } = Array.AsReadOnly(new OfflineSqlDialect[] {
        new("ansi", "ANSI SQL"),
        new("access", "Access"),
        new("db2", "Db2"),
        new("duckdb", "DuckDB"),
        new("firebird", "Firebird"),
        new("mysql", "MySQL"),
        new("singlestore", "SingleStore"),
        new("oracle", "Oracle"),
        new("cockroachdb", "CockroachDB"),
        new("cratedb", "CrateDB"),
        new("postgresql", "PostgreSQL"),
        new("snowflake", "Snowflake"),
        new("sqlite", "SQLite"),
        new("sqlserver", "SQL Server"),
        new("teradata", "Teradata")
    });
    private static readonly Lazy<IDialectRegistry> Registry = new(() => {
        var builder = new DialectRegistryBuilder();
        builder.AddDialect<DubUrl.Querying.Dialects.AnsiDialect>(["ansi"]);
        builder.AddDialect<DubUrl.Provider.Access.Sql.MsAccessDialect>(["access"]);
        builder.AddDialect<DubUrl.Provider.Db2.Sql.Db2Dialect>(["db2"]);
        builder.AddDialect<DubUrl.Provider.DuckDb.Sql.DuckDbDialect>(["duckdb"]);
        builder.AddDialect<DubUrl.Provider.FirebirdSql.Sql.FirebirdSqlDialect>(["firebird"]);
        builder.AddDialect<DubUrl.Provider.MySql.Sql.MySqlDialect>(["mysql"]);
        builder.AddDialect<DubUrl.Provider.MySql.Sql.SingleStoreDialect>(["singlestore"]);
        builder.AddDialect<DubUrl.Provider.Oracle.Sql.OracleDialect>(["oracle"]);
        builder.AddDialect<DubUrl.Provider.PostgreSql.Sql.CockRoachDialect>(["cockroachdb"]);
        builder.AddDialect<DubUrl.Provider.PostgreSql.Sql.CrateDbDialect>(["cratedb"]);
        builder.AddDialect<DubUrl.Provider.PostgreSql.Sql.PgsqlDialect>(["postgresql"]);
        builder.AddDialect<DubUrl.Provider.Snowflake.Sql.SnowflakeDialect>(["snowflake"]);
        builder.AddDialect<DubUrl.Provider.Sqlite.Sql.SqliteDialect>(["sqlite"]);
        builder.AddDialect<DubUrl.Provider.SqlServer.Sql.TSqlDialect>(["sqlserver"]);
        builder.AddDialect<DubUrl.Provider.Teradata.Sql.TeradataDialect>(["teradata"]);
        return builder.Build();
    });
    public static IDialect Resolve(string id)
    {
        if (!Catalog.Any(d => d.Id == id)) throw new ArgumentException("Unknown or unavailable --dialect. Supported IDs: " + string.Join(", ", Catalog.Select(d => d.Id)));
        return Registry.Value.Get(id);
    }
}
