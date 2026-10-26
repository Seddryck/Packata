using System.Data;
using System.Globalization;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ResourceReaders.Providers;

namespace Packata.ResourceReaders;

/// <summary>Operation-scoped expansion; never mutates the contract or recursively decodes lookups.</summary>
internal sealed class CategoryTableLoader(IEndpointStreamResolver streams, DataFormatResolver formats,
    IReadOnlyList<IDataEndpointReaderProvider> providers, CategoryDecodingOptions options)
{
    private const string StringType = "string";
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
            fields.Add(await ExpandFieldAsync(request, field, token).ConfigureAwait(false));
        }
        return request with { Schema = request.Schema with { Fields = fields } };
    }

    private async ValueTask<DataField> ExpandFieldAsync(DataEndpointReadRequest request, DataField field, CancellationToken token)
    {
        if (field.ValueDomain?.Reference is { EffectiveEncoding: ValueEncoding.Label })
            return field with { LogicalType = StringType, Format = null, Constraints = [], ValueDomain = null };
        if (field.ValueDomain?.Reference is not { EffectiveEncoding: ValueEncoding.Value } reference) return field;
        if (reference.Package is not null) throw Error(reference.ResourceName, "external-package references are unsupported");
        var contract = request.Contract ?? throw Error(reference.ResourceName, "supply the containing contract in DataEndpointReadRequest.Contract");
        var asset = Resolve(contract, reference);
        CheckCycles(contract, asset, new HashSet<string>(StringComparer.Ordinal), token);
        ValidateValueType(asset, field);
        if (!_loaded.TryGetValue(asset.Id, out var entries))
        {
            entries = await LoadAsync(contract, asset, token).ConfigureAwait(false);
            _loaded.Add(asset.Id, entries);
        }
        // This schema is presentation-only. Retain the contract's original restrictions and reference.
        return field with { Constraints = [], ValueDomain = new ValueDomain(entries,
            entries.Select(entry => entry.Value), field.ValueDomain.Ordered) };
    }

    private static void ValidateValueType(DataAsset asset, DataField field)
    {
        var valueField = MappedField(asset, asset.CategoryTable!.ValueField);
        if (field.LogicalType != valueField.LogicalType || field.LogicalType is not (StringType or "integer"))
            throw Error(asset.Name, $"mapped values are incompatible with field '{field.Name}' ({field.LogicalType})");
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
        if (valueField.LogicalType is not (StringType or "integer")) throw Error(asset.Name, "mapped values must be strings or integers");
        var labels = CategoryLabelSelection.Resolve(asset, options.Language, options.Label, options.Diagnostic);
        var defaultLabel = table.LabelField is null ? null : MappedField(asset, table.LabelField);
        var request = LookupRequest(contract, asset);
        var format = formats.Resolve(request.Endpoint);
        var provider = LookupProvider(request, format, asset.Name);
        var paths = (PathLocation)request.Endpoint.Location;
        var opened = new List<Stream>();
        try
        {
            foreach (var path in paths.Paths)
                await CopyLookupAsync(path, asset.Name, opened, token).ConfigureAwait(false);
            using var reader = await provider.OpenAsync(new ReaderOpenContext(request, format, opened), token).ConfigureAwait(false);
            return ReadEntries(reader, asset.Name, valueField, defaultLabel, labels, token);
        }
        finally
        {
            foreach (var stream in opened) await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static DataEndpointReadRequest LookupRequest(DataContract contract, DataAsset asset)
    {
        if (asset.EndpointBindings.Count != 1) throw Error(asset.Name, "exactly one lookup endpoint is required");
        var binding = asset.EndpointBindings[0];
        var endpoints = contract.Endpoints.Where(endpoint => endpoint.Id == binding.EndpointId).ToArray();
        if (endpoints.Length != 1) throw Error(asset.Name, "lookup endpoint could not be resolved uniquely");
        var schema = asset.Schema!;
        var rawSchema = schema with { Fields = schema.Fields.Select(field =>
            field with { LogicalType = StringType, Format = null, Constraints = [], ValueDomain = null }).ToArray() };
        return new(endpoints[0], rawSchema, binding.AssetPath);
    }

    private IDataEndpointReaderProvider LookupProvider(DataEndpointReadRequest request, ResolvedDataFormat format, string resource)
    {
        var provider = providers.FirstOrDefault(candidate => candidate.CanHandle(request, format));
        if (provider is not DelimitedReaderProvider || request.Endpoint.Location is not PathLocation { Paths.Count: > 0 }
            || format.Compression is not null)
            throw Error(resource, "only local uncompressed PocketCsvReader delimited lookup resources are supported");
        return provider;
    }

    private async ValueTask CopyLookupAsync(string path, string resource, List<Stream> opened, CancellationToken token)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            throw Error(resource, "lookup paths must be local; downloads are unsupported");
        await using var source = await streams.OpenAsync(path, token).ConfigureAwait(false);
        var buffer = new byte[8192];
        var copy = new MemoryStream();
        opened.Add(copy);
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            _bytes += count;
            if (_bytes > options.ByteLimit) throw Error(resource, $"category-table byte limit ({options.ByteLimit}) exceeded");
            await copy.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        copy.Position = 0;
    }

    private IReadOnlyList<ValueOption> ReadEntries(IDataReader reader, string resource, DataField valueField,
        DataField? defaultLabel, IReadOnlyList<DataField> labelFields, CancellationToken token)
    {
        var result = new List<ValueOption>();
        var values = new HashSet<ScalarValue>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!reader.Read()) break;
            token.ThrowIfCancellationRequested();
            if (++_rows > options.RowLimit) throw Error(resource, $"category-table row limit ({options.RowLimit}) exceeded");
            var value = ReadValue(reader, valueField, resource);
            if (!values.Add(value)) throw Error(resource, "duplicate mapped value");
            ValidateDefaultLabel(reader, defaultLabel, labels, resource);
            var label = labelFields.Select(field => LabelText(reader, reader.GetOrdinal(field.Name), field)).FirstOrDefault(text => text is not null);
            result.Add(new(value, label));
        }
        return result;
    }

    private static ScalarValue ReadValue(IDataReader reader, DataField field, string resource)
    {
        var index = reader.GetOrdinal(field.Name);
        var text = reader.IsDBNull(index) ? null : reader.GetString(index);
        if (string.IsNullOrEmpty(text)) throw Error(resource, "missing required mapped value");
        if (field.LogicalType != "integer") return ScalarValue.From(text);
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            throw Error(resource, $"mapped value is incompatible with integer column '{field.Name}'");
        return ScalarValue.From(integer);
    }

    private static void ValidateDefaultLabel(IDataReader reader, DataField? field, HashSet<string> labels, string resource)
    {
        if (field is null) return;
        var text = LabelText(reader, reader.GetOrdinal(field.Name), field);
        if (text is not null && !labels.Add(text)) throw Error(resource, "duplicate non-null mapped label");
    }

    private static CategoryTableDecodingException Error(string resource, string message) => new(resource, message);

    private static string? LabelText(IDataReader reader, int index, DataField field)
    {
        if (reader.IsDBNull(index)) return null;
        var text = reader.GetString(index);
        if (field.Extensions.TryGetNamespace("datapackage", out var metadata) &&
            metadata.TryGetValue("missingValues", out var raw) && raw is System.Collections.IEnumerable missing &&
            missing.Cast<object?>().Any(value => value is string marker && marker == text))
            return null;
        return text;
    }
}
