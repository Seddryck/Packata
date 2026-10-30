using System.Data;
using System.Data.Common;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Application;

public sealed record DataPreviewOptions(int Limit = 20, IReadOnlyList<string>? Columns = null,
    bool RequireUniqueColumnNames = true);
public sealed record DataPreviewColumn(string Name, string Type);
public sealed record DataPreviewSample(IReadOnlyList<DataPreviewColumn> Columns, IReadOnlyList<object?[]> Rows);
public enum DataPreviewSelectionFailure { Binding, Endpoint, Column }

/// <summary>A selection failure that hosts can project into their own diagnostics.</summary>
public sealed class DataPreviewSelectionException(DataPreviewSelectionFailure failure, string message) : Exception(message)
{
    public DataPreviewSelectionFailure Failure { get; } = failure;
}

/// <summary>
/// Coordinates explicit, bounded data preview. Hosts supply reader capabilities and access policy;
/// the service owns each opened reader for the duration of the operation.
/// </summary>
public sealed class DataPreviewService(IDataEndpointReaderFactory readers)
{
    /// <summary>Resolves a binding without acquiring credentials, connectors or resource data.</summary>
    public static DataEndpointReadRequest SelectRequest(DataContract contract, DataAsset asset, int? bindingIndex = null)
    {
        if (bindingIndex is null && asset.EndpointBindings.Count != 1)
            throw new DataPreviewSelectionException(DataPreviewSelectionFailure.Binding,
                $"Asset '{asset.Id}' must have exactly one endpoint binding for preview; none or multiple bindings cannot be selected automatically.");
        var index = bindingIndex ?? 0;
        if (index < 0 || index >= asset.EndpointBindings.Count)
            throw new DataPreviewSelectionException(DataPreviewSelectionFailure.Binding, "The selected endpoint binding does not exist.");
        var binding = asset.EndpointBindings[index];
        var endpoints = contract.Endpoints.Where(endpoint => endpoint.Id == binding.EndpointId).ToArray();
        if (endpoints.Length != 1)
            throw new DataPreviewSelectionException(DataPreviewSelectionFailure.Endpoint,
                $"Endpoint '{binding.EndpointId}' for asset '{asset.Id}' could not be resolved uniquely.");
        return new(endpoints[0], asset.Schema, binding.AssetPath ?? asset.PhysicalName ?? asset.Name, contract);
    }

    public async Task<DataPreviewSample> ReadAsync(DataEndpointReadRequest request, DataPreviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Limit);
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = await readers.OpenAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // Streaming readers can discover headers/schema on their first read.
        var hasRow = await NextAsync(reader, cancellationToken).ConfigureAwait(false);
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        if (!hasRow && names.Length == 0 && request.Schema is { Fields.Count: > 0 } schema)
            names = schema.Fields.Select(field => field.Name).ToArray();
        var ordinals = SelectColumns(names, options);
        var columns = ordinals.Select(index => new DataPreviewColumn(names[index],
            DeclaredType(request.Schema, names[index]) ?? (hasRow ? reader.GetFieldType(index)?.Name : null) ?? "unknown")).ToArray();
        var rows = new List<object?[]>();
        while (hasRow)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(ordinals.Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
            if (rows.Count == options.Limit) break;
            hasRow = await NextAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(columns, rows);
    }

    private static int[] SelectColumns(string[] names, DataPreviewOptions options)
    {
        // Positional viewers can retain repeated headers; named projection must always be unique.
        if (options.Columns is null && !options.RequireUniqueColumnNames)
            return Enumerable.Range(0, names.Length).ToArray();
        return (options.Columns ?? names).Select(name =>
        {
            var matches = Enumerable.Range(0, names.Length).Where(index => names[index] == name).ToArray();
            return matches.Length == 1 ? matches[0] : throw new DataPreviewSelectionException(DataPreviewSelectionFailure.Column,
                $"Column '{name}' is unknown or ambiguous. Select exact, unique field names.");
        }).ToArray();
    }

    private static string? DeclaredType(DataSchema? schema, string name)
    {
        var field = schema?.Fields.FirstOrDefault(value => value.Name == name);
        return field?.LogicalType ?? field?.PhysicalType;
    }

    private static async ValueTask<bool> NextAsync(IDataReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasRow = reader is DbDataReader asyncReader
            ? await asyncReader.ReadAsync(cancellationToken).ConfigureAwait(false) : reader.Read();
        cancellationToken.ThrowIfCancellationRequested();
        return hasRow;
    }
}
