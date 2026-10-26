using System.Globalization;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.Providers;

namespace Packata.ResourceReaders;

/// <summary>Operation-scoped expansion; never mutates the contract or recursively decodes lookups.</summary>
internal sealed class CategoryTableLoader(IEndpointStreamResolver streams, DataFormatResolver formats,
    IReadOnlyList<IDataEndpointReaderProvider> providers, int rowLimit, long byteLimit)
{
    private readonly Dictionary<string, IReadOnlyList<ValueOption>> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private long _rows;
    private long _bytes;

    public async ValueTask<DataEndpointReadRequest> ExpandAsync(DataEndpointReadRequest request, CancellationToken token)
    {
        if (request.Schema is null) return request;
        var fields = new List<DataField>();
        foreach (var field in request.Schema.Fields)
        {
            token.ThrowIfCancellationRequested();
            if (field.ValueDomain?.Reference is { EffectiveEncoding: ValueEncoding.Label })
            {
                // The recipe retains the value's logical type even when the stored cells are labels.
                fields.Add(field with { LogicalType = "string", Format = null, Constraints = [], ValueDomain = null });
                continue;
            }
            if (field.ValueDomain?.Reference is not { EffectiveEncoding: ValueEncoding.Value } reference)
            {
                fields.Add(field);
                continue;
            }
            if (reference.Package is not null) throw Error(reference.ResourceName, "external-package references are unsupported");
            var contract = request.Contract ?? throw Error(reference.ResourceName, "supply the containing contract in DataEndpointReadRequest.Contract");
            var asset = Resolve(contract, reference);
            CheckCycles(contract, asset, new HashSet<string>(StringComparer.Ordinal), token);
            var valueField = MappedField(asset, asset.CategoryTable!.ValueField);
            if (field.LogicalType != valueField.LogicalType || field.LogicalType is not ("string" or "integer"))
                throw Error(asset.Name, $"mapped values are incompatible with field '{field.Name}' ({field.LogicalType})");
            if (!_loaded.TryGetValue(asset.Id, out var entries))
            {
                entries = await LoadAsync(contract, asset, token).ConfigureAwait(false);
                _loaded.Add(asset.Id, entries);
            }
            // This schema is presentation-only. Retain the contract's original restrictions and reference.
            fields.Add(field with { Constraints = [], ValueDomain = new ValueDomain(entries,
                entries.Select(entry => entry.Value), field.ValueDomain.Ordered) });
        }
        return request with { Schema = request.Schema with { Fields = fields } };
    }

    private static DataAsset Resolve(DataContract contract, ValueDomainReference reference)
    {
        if (reference.Package is not null) throw Error(reference.ResourceName, "external-package references are unsupported");
        var matches = contract.Assets.Where(asset => reference.BoundAssetId is { } id
            ? asset.Id == id : asset.Name == reference.ResourceName).ToArray();
        if (matches.Length != 1 || matches[0].CategoryTable is null || matches[0].Schema is null)
            throw Error(reference.ResourceName, "the named local category table could not be resolved uniquely");
        return matches[0];
    }

    private void CheckCycles(DataContract contract, DataAsset asset, HashSet<string> visiting, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_checked.Contains(asset.Id)) return;
        if (!visiting.Add(asset.Id)) throw Error(asset.Name, "cyclic category-table references are unsupported");
        foreach (var field in asset.Schema!.Fields)
            if (field.ValueDomain?.Reference is { } reference)
                CheckCycles(contract, Resolve(contract, reference), visiting, token);
        visiting.Remove(asset.Id);
        _checked.Add(asset.Id);
    }

    private static DataField MappedField(DataAsset asset, FieldPath path)
    {
        var fields = path.Segments.Count == 1
            ? asset.Schema!.Fields.Where(field => field.Name == path.Segments[0]).ToArray() : [];
        return fields.Length == 1 ? fields[0] : throw Error(asset.Name, "mapped columns must name unique top-level fields");
    }

    private async ValueTask<IReadOnlyList<ValueOption>> LoadAsync(DataContract contract, DataAsset asset, CancellationToken token)
    {
        var table = asset.CategoryTable!;
        var valueField = MappedField(asset, table.ValueField);
        if (valueField.LogicalType is not ("string" or "integer")) throw Error(asset.Name, "mapped values must be strings or integers");
        var labelField = table.LabelField is null ? null : MappedField(asset, table.LabelField);
        if (labelField is not null && labelField.LogicalType != "string") throw Error(asset.Name, "mapped labels must be strings");
        if (asset.EndpointBindings.Count != 1) throw Error(asset.Name, "exactly one lookup endpoint is required");
        var binding = asset.EndpointBindings[0];
        var endpoints = contract.Endpoints.Where(endpoint => endpoint.Id == binding.EndpointId).ToArray();
        if (endpoints.Length != 1) throw Error(asset.Name, "lookup endpoint could not be resolved uniquely");
        var endpoint = endpoints[0];
        var rawSchema = asset.Schema! with { Fields = asset.Schema!.Fields.Select(field =>
            field with { LogicalType = "string", Format = null, Constraints = [], ValueDomain = null }).ToArray() };
        var request = new DataEndpointReadRequest(endpoint, rawSchema, binding.AssetPath);
        var format = formats.Resolve(endpoint);
        var provider = providers.FirstOrDefault(candidate => candidate.CanHandle(request, format));
        if (provider is not DelimitedReaderProvider || endpoint.Location is not PathLocation { Paths.Count: > 0 } paths
            || format.Compression is not null)
            throw Error(asset.Name, "only local uncompressed PocketCsvReader delimited lookup resources are supported");
        var opened = new List<Stream>();
        try
        {
            foreach (var path in paths.Paths)
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
                    throw Error(asset.Name, "lookup paths must be local; downloads are unsupported");
                using var source = await streams.OpenAsync(path, token).ConfigureAwait(false);
                var buffer = new byte[8192];
                var copy = new MemoryStream();
                opened.Add(copy);
                int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    _bytes += count;
                    if (_bytes > byteLimit) throw Error(asset.Name, $"category-table byte limit ({byteLimit}) exceeded");
                    await copy.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                copy.Position = 0;
            }
            using var reader = await provider.OpenAsync(new ReaderOpenContext(request, format, opened), token).ConfigureAwait(false);
            var result = new List<ValueOption>();
            var values = new HashSet<ScalarValue>();
            var labels = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!reader.Read()) break;
                token.ThrowIfCancellationRequested();
                if (++_rows > rowLimit) throw Error(asset.Name, $"category-table row limit ({rowLimit}) exceeded");
                var valueIndex = reader.GetOrdinal(valueField.Name);
                var text = reader.IsDBNull(valueIndex) ? null : reader.GetString(valueIndex);
                if (string.IsNullOrEmpty(text)) throw Error(asset.Name, "missing required mapped value");
                ScalarValue value;
                if (valueField.LogicalType == "integer")
                {
                    if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                        throw Error(asset.Name, $"mapped value is incompatible with integer column '{valueField.Name}'");
                    value = ScalarValue.From(integer);
                }
                else value = ScalarValue.From(text);
                if (!values.Add(value)) throw Error(asset.Name, "duplicate mapped value");
                string? label = null;
                if (labelField is not null)
                {
                    var index = reader.GetOrdinal(labelField.Name);
                    label = reader.IsDBNull(index) ? null : reader.GetString(index);
                    if (label is not null && !labels.Add(label)) throw Error(asset.Name, "duplicate non-null mapped label");
                }
                result.Add(new(value, label));
            }
            return result;
        }
        finally
        {
            foreach (var stream in opened) stream.Dispose();
        }
    }

    private static CategoryTableDecodingException Error(string resource, string message) => new(resource, message);
}
