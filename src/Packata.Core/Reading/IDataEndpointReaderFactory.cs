using System.Data;
using Packata.Core.Contracts;

namespace Packata.Core.Reading;

/// <summary>Opens tabular data from a canonical endpoint without retaining request state.</summary>
public interface IDataEndpointReaderFactory
{
    ValueTask<IDataReader> OpenAsync(DataEndpoint endpoint, DataSchema? schema = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves canonical path values to owned streams.</summary>
public interface IEndpointStreamResolver
{
    ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default);
}
