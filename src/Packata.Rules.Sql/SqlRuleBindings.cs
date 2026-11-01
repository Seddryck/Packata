#nullable enable
using System.Data;
using Packata.Core.Contracts;
using Packata.Database;

namespace Packata.Rules.Sql;

public enum SqlDialect { Ansi,
    Sqlite,
    PostgreSql,
    SqlServer,
    MySql,
    DuckDb }
public enum SqlReadOnlyCapability { Unknown,
    EnforcedByProvider,
    ReadOnlyCredentials }
public sealed record SqlValueParameter(string Name, QualityValue Value, DbType? Type = null);
public sealed record SqlRuleOptions(SqlDialect? AuthoredDialect = null, IReadOnlyList<string>? ObjectPath = null,
    IReadOnlyList<string>? PropertyPath = null, IReadOnlyList<SqlValueParameter>? Parameters = null,
    int CommandTimeoutSeconds = 30, bool RequireReadOnly = true);

/// <summary>Every open returns a per-run session. Borrowed connections and transactions remain caller-owned.</summary>
public interface ISqlRuleConnectionSource
{
    ValueTask<SqlRuleSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken);
}

public sealed class SqlRuleSession(IDbConnection connection, Func<string, string> quoteIdentifier,
    SqlReadOnlyCapability readOnlyCapability, bool ownsConnection = true, IDbTransaction? transaction = null,
    bool ownsTransaction = false) : IDisposable
{
    public IDbConnection Connection { get; } = connection;
    public Func<string, string> QuoteIdentifier { get; } = quoteIdentifier;
    public SqlReadOnlyCapability ReadOnlyCapability { get; } = readOnlyCapability;
    public IDbTransaction? Transaction { get; } = transaction;
    public void Dispose()
    {
        try { if (ownsTransaction) Transaction?.Dispose(); }
        finally { if (ownsConnection) Connection.Dispose(); }
    }
}

/// <summary>Reuses Packata's database service; the host explicitly supplies the session's read-only guarantee.</summary>
public sealed class DatabaseSqlRuleConnectionSource(IDatabaseConnectionService databases,
    SqlReadOnlyCapability readOnlyCapability = SqlReadOnlyCapability.Unknown) : ISqlRuleConnectionSource
{
    public async ValueTask<SqlRuleSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken)
    {
        var session = await databases.OpenAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return new(session.Connection, session.RenderIdentifier, readOnlyCapability);
    }
}
