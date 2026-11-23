using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders;

/// <summary>Opens canonical endpoints matched by a resource reader provider.</summary>
/// <remarks>
/// Implementations must be safe for concurrent calls. Streams supplied through <see cref="ReaderOpenContext"/>
/// remain owned by the factory and must not be disposed by the provider. Any additional resources opened by a
/// provider must be owned by the returned reader and released when that reader is disposed.
/// </remarks>
public interface IDataEndpointReaderProvider
{
    /// <summary>Identifies this reader to separately packaged field binding providers.</summary>
    /// <value>The provider’s stable binding key.</value>
    string BindingKey => GetType().FullName ?? GetType().Name;

    /// <summary>Requests a default representation for fields with an explicit binding contract.</summary>
    Type? GetDefaultRepresentation(DataField field) => null;

    /// <summary>Returns decoded field text before any CLR conversion when the reader exposes it.</summary>
    /// <remarks>Readers without pre-conversion text access apply missing-value markers in their native bindings.</remarks>
    string? GetUnconvertedText(IDataRecord row, int ordinal) => null;

    /// <summary>Returns whether this provider handles the request. Matching must not acquire resources.</summary>
    bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format);

    /// <summary>Opens a reader for a previously matched request.</summary>
    ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Opens an explicit indexed lookup without the factory opening source streams ahead of pruning.</summary>
public interface IDataEndpointIndexedReaderProvider : IDataEndpointReaderProvider
{
    /// <summary>Applies every equality using the required index, or fails when the reader cannot use it.</summary>
    ValueTask<IDataReader> OpenIndexedAsync(IndexedReaderOpenContext context, CancellationToken cancellationToken = default);
}

public sealed record IndexedReaderOpenContext(
    DataEndpointReadRequest Request,
    ResolvedDataFormat Format,
    IEndpointStreamResolver StreamResolver,
    IReadOnlyList<ResolvedIndexedEquality> Equalities)
{
    /// <summary>Selected native field bindings.</summary>
    /// <value>The selected bindings, keyed by canonical schema ordinal.</value>
    public IReadOnlyDictionary<int, ReaderFieldBinding> FieldBindings { get; init; } =
        System.Collections.ObjectModel.ReadOnlyDictionary<int, ReaderFieldBinding>.Empty;
}

public sealed record ResolvedIndexedEquality(DataIndex Index, DataField Field, int Ordinal, object Value);

/// <summary>Supplies a provider with the canonical request, resolved format, and factory-owned streams.</summary>
public sealed record ReaderOpenContext(
    DataEndpointReadRequest Request,
    ResolvedDataFormat Format,
    IReadOnlyList<Stream> Streams)
{
    public DataEndpoint Endpoint => Request.Endpoint;
    public DataSchema? Schema => Request.Schema;

    /// <summary>Selected native field bindings.</summary>
    /// <value>The selected bindings, keyed by canonical schema ordinal.</value>
    public IReadOnlyDictionary<int, ReaderFieldBinding> FieldBindings { get; init; } =
        System.Collections.ObjectModel.ReadOnlyDictionary<int, ReaderFieldBinding>.Empty;
}

/// <summary>Contains normalized format information resolved by the reader factory.</summary>
public sealed record ResolvedDataFormat(
    string Name,
    string? MediaType,
    string? Compression);
