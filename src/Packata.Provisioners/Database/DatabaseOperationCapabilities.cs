using DubUrl;
using DubUrl.Querying.Dialects;

namespace Packata.Provisioners.Database;

public enum DatabaseOperation { Reading,
    SchemaDeployment,
    BulkLoading }

public sealed record DatabaseOperationSupport(bool Reading, bool SchemaDeployment, bool BulkLoading)
{
    public bool Supports(DatabaseOperation operation) => operation switch
    {
        DatabaseOperation.Reading => Reading,
        DatabaseOperation.SchemaDeployment => SchemaDeployment,
        DatabaseOperation.BulkLoading => BulkLoading,
        _ => false
    };
}

/// <summary>Host policy for known operations; does not guarantee permissions, connectivity or rollback.</summary>
public interface IDatabaseOperationCapabilities
{
    DatabaseOperationSupport GetSupport(ConnectionUrl connection);
}

/// <summary>Known Packata DDL support, independent of upstream connector and bulk-copy capabilities.</summary>
public sealed class DubUrlOperationCapabilities : IDatabaseOperationCapabilities
{
    private static readonly HashSet<string> SchemaDialects = new(StringComparer.Ordinal)
    {
        "DubUrl.Provider.SqlServer.Sql.TSqlDialect", "DubUrl.Provider.DuckDb.Sql.DuckDbDialect",
        "DubUrl.Provider.PostgreSql.Sql.PgsqlDialect", "DubUrl.Provider.Sqlite.Sql.SqliteDialect",
        "DubUrl.Provider.MySql.Sql.MySqlDialect", "DubUrl.Provider.Oracle.Sql.OracleDialect"
    };
    public DatabaseOperationSupport GetSupport(ConnectionUrl connection)
    {
        var dialect = connection.Dialect;
        // ANSI fallback and arbitrary custom dialects require an explicit host policy.
        var ddl = connection.TryGetCapability<DatabaseSchemaDeploymentSupport>(out var custom)
            ? custom!.Supported : SchemaDialects.Contains(dialect.GetType().FullName ?? "");
        var bulk = connection.BulkCopyProvider is not null;
        return new(true, ddl, bulk);
    }
}

/// <summary>Optional explicit Packata schema support declared on a DubUrl provider registration.</summary>
public sealed record DatabaseSchemaDeploymentSupport(bool Supported);

public sealed class DatabaseOperationNotSupportedException(DatabaseOperation operation, string provider)
    : NotSupportedException($"Provider '{provider}' does not support Packata operation '{operation}'. Configure a supported connector or an explicit operation capability policy.")
{
    public string Code => "database-operation-unsupported";
    public DatabaseOperation Operation { get; } = operation;
}
