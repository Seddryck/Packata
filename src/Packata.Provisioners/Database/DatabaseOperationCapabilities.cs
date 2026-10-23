using DubUrl;
using DubUrl.Mapping;
using DubUrl.Querying.Dialects;

namespace Packata.Provisioners.Database;

public enum DatabaseOperation { Reading, SchemaDeployment, BulkLoading }

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

/// <summary>Conservative checks for the adapters shipped by DubUrl 0.29/0.30.</summary>
public sealed class DubUrlOperationCapabilities : IDatabaseOperationCapabilities
{
    public DatabaseOperationSupport GetSupport(ConnectionUrl connection)
    {
        var dialect = connection.Dialect;
        // ANSI fallback and arbitrary custom dialects require an explicit host policy.
        var ddl = dialect.GetType() == typeof(TSqlDialect) || dialect.GetType() == typeof(DuckDbDialect)
            || dialect.GetType() == typeof(PgsqlDialect) || dialect.GetType() == typeof(SqliteDialect)
            || dialect.GetType() == typeof(MySqlDialect) || dialect.GetType() == typeof(OracleDialect);
        var bulk = false;
        if (connection.Connectivity is IDirectConnectivity && dialect is TSqlDialect or DuckDbDialect)
        {
            // An ODBC mapping with the same SQL dialect cannot use a native driver's bulk API.
            // Inspect an unopened, operation-owned connection before any DDL or source reading.
            using var probe = connection.Connect();
            var type = probe.GetType();
            bulk = dialect is DuckDbDialect
                ? HasDuckDbAppender(type)
                : (type.Assembly.GetType("Microsoft.Data.SqlClient.SqlBulkCopy")
                    ?? type.Assembly.GetType("System.Data.SqlClient.SqlBulkCopy"))?.GetConstructor([type]) is not null;
        }
        return new(true, ddl, bulk);
    }

    private static bool HasDuckDbAppender(Type connectionType)
    {
        try { return connectionType.GetMethod("CreateAppender", [typeof(string)]) is not null; }
        catch (System.Reflection.AmbiguousMatchException) { return false; }
    }
}

public sealed class DatabaseOperationNotSupportedException(DatabaseOperation operation, string provider)
    : NotSupportedException($"Provider '{provider}' does not support Packata operation '{operation}'. Configure a supported connector or an explicit operation capability policy.")
{
    public string Code => "database-operation-unsupported";
    public DatabaseOperation Operation { get; } = operation;
}
