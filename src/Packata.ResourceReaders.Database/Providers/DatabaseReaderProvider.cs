using System.Data;
using System.Data.Common;
using Packata.Database;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Database.Providers;

internal sealed class DatabaseReaderProvider : IDataEndpointReaderProvider
{
    private readonly IDatabaseConnectionService _connections;
    private readonly IConnectorAvailabilityService? _availability;
    public DatabaseReaderProvider(IDatabaseConnectionService connections, IConnectorAvailabilityService? availability = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _availability = availability;
    }
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is ConnectionLocation;

    public async ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = context.Endpoint;
        var table = context.Request.AssetPath ?? OptionString(endpoint.Format, "table");
        if (string.IsNullOrWhiteSpace(table))
            throw new ArgumentException("A database read requires a nonempty asset path or table format option.", nameof(endpoint));
        endpoint = await PrepareEndpointAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var session = await _connections.OpenAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var connection = session.Connection;
        IDbCommand? command = null;
        try
        {
            command = connection.CreateCommand();
            command.CommandText = BuildQuery(endpoint, table, session.RenderIdentifier);
            return await ExecuteOwnedReaderAsync(command, connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try { command?.Dispose(); }
            finally { connection.Dispose(); }
            if (error is ArgumentException or OperationCanceledException or DatabaseConnectionException) throw;
            throw new DatabaseConnectionException("database-query-failed",
                "The database query failed. Check the bound table, namespace, server permissions and connectivity.") { EndpointId = endpoint.Id };
        }
    }

    private async ValueTask<DataEndpoint> PrepareEndpointAsync(DataEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (_availability is null) return endpoint;
        var location = (ConnectionLocation)endpoint.Location;
        var url = DatabaseEndpointResolver.BuildConnectionUrl(location);
        var scheme = new Uri(url).Scheme;
        var resolvedScheme = await _availability.EnsureAvailableAsync(scheme, cancellationToken).ConfigureAwait(false);
        if (scheme.Equals(resolvedScheme, StringComparison.OrdinalIgnoreCase)) return endpoint;
        return endpoint with { Location = location with { Scheme = resolvedScheme,
            ConnectionUrl = location.ConnectionUrl is null ? null : resolvedScheme + url[url.IndexOf("://", StringComparison.Ordinal)..] } };
    }

    private static string BuildQuery(DataEndpoint endpoint, string table, Func<string, string> renderIdentifier)
    {
        var ns = ((ConnectionLocation)endpoint.Location).Namespace ?? OptionString(endpoint.Format, "namespace");
        var identifiers = new[] { OptionString(endpoint.Format, "catalog"),
            OptionString(endpoint.Format, "databaseQualifier"), ns, table }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => renderIdentifier(value!));
#pragma warning disable S2077 // Every object identifier segment is quoted by the provider.
        return $"SELECT * FROM {string.Join('.', identifiers)}";
#pragma warning restore S2077
    }

    private static async ValueTask<IDataReader> ExecuteOwnedReaderAsync(IDbCommand command, IDbConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reader = command is DbCommand asyncCommand
            ? await asyncCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)
            : command.ExecuteReader();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new OwnedDatabaseReader(reader, command, connection);
        }
        catch { reader.Dispose(); throw; }
    }

    private static string? OptionString(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true ? value?.ToString() : null;
}
