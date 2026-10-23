using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Database.Providers;

internal sealed class DatabaseReaderProvider(IDatabaseSessionFactory databases) : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is ConnectionLocation;

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = context.Endpoint;
        var location = (ConnectionLocation)endpoint.Location;
        var session = databases.Open(location);
        var connection = session.Connection;
        IDbCommand? command = null;
        try
        {
            command = connection.CreateCommand();
            var table = context.Request.AssetPath ?? OptionString(endpoint.Format, "table")
                ?? throw new ArgumentException(
                    "A database read requires an asset path or table format option.", nameof(endpoint));
            var ns = location.Namespace ?? OptionString(endpoint.Format, "namespace");
            var identifiers = new[]
            {
                OptionString(endpoint.Format, "catalog"),
                OptionString(endpoint.Format, "databaseQualifier"),
                ns,
                table
            }.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => session.RenderIdentifier(value!));
#pragma warning disable S2077 // Object identifiers cannot be parameters; every segment is provider-quoted above.
            command.CommandText = $"SELECT * FROM {string.Join('.', identifiers)}";
#pragma warning restore S2077
            var reader = command.ExecuteReader();
            return ValueTask.FromResult<IDataReader>(new OwnedDatabaseReader(reader, command, connection));
        }
        catch
        {
            command?.Dispose();
            connection.Dispose();
            throw;
        }
    }

    private static string? OptionString(DataFormat? format, string name) =>
        format?.Options.TryGetValue(name, out var value) == true ? value?.ToString() : null;
}
