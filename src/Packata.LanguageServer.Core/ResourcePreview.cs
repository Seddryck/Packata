using System.Data;
using System.Data.Common;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Application;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Excel;
using Packata.Storages;
using YamlDotNet.RepresentationModel;

namespace Packata.LanguageServer.Core;

/// <summary>Metadata-only eligibility checks and explicit, bounded resource reads.</summary>
public static class ResourcePreview
{
    public const int DefaultLimit = 20;
    private const string Unsupported = "Preview supports local and HTTP/HTTPS files without custom authentication headers only.";
    private sealed record Selection(PreviewTarget Target, DataEndpointReadRequest? Request);

    public static IReadOnlyList<PreviewTarget> Targets(ContractDocument document, DataContract contract, Uri baseUri)
        => Selections(document, contract, baseUri).Select(value => value.Target).ToArray();

    private static IEnumerable<Selection> Selections(ContractDocument document, DataContract contract, Uri baseUri)
    {
        var index = new SourceIndex(document.Root);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(contract))));
        for (var assetIndex = 0; assetIndex < contract.Assets.Count; assetIndex++)
        {
            var asset = contract.Assets[assetIndex];
            var source = $"/{(document.Format == "odcs" ? "schema" : "resources")}/{assetIndex}";
            if (asset.EndpointBindings.Count == 0)
                yield return new(new(source, null, ContractJson.Redact(asset.Name), false,
                    "No unambiguous endpoint binding. Select or configure a single server in the descriptor."), null);
            for (var bindingIndex = 0; bindingIndex < asset.EndpointBindings.Count; bindingIndex++)
            {
                var binding = asset.EndpointBindings[bindingIndex];
                var endpoints = contract.Endpoints.Where(value => value.Id == binding.EndpointId).ToArray();
                var endpoint = endpoints.Length == 1 ? endpoints[0] : null;
                if (endpoint?.Location is PathLocation paths)
                {
                    try { endpoint = endpoint with { Location = new PathLocation(paths.Paths.Select(path => Resolve(baseUri, path).AbsoluteUri).ToArray()) }; }
                    catch (UriFormatException) { endpoint = null; }
                }
                var request = endpoint is null ? null : new DataEndpointReadRequest(endpoint, asset.Schema,
                    binding.AssetPath ?? asset.PhysicalName ?? asset.Name, contract);
                var reason = request is null ? "The endpoint binding cannot be resolved uniquely."
                    : Eligibility(request, baseUri);
                // Authentication declarations can be native properties omitted by canonical mapping.
                var servers = ContractDocument.Child(document.Root, "servers") as YamlSequenceNode;
                var serverNodes = servers?.Children.Where(node => endpoint is not null &&
                    ((ContractDocument.Child(node, "server") as YamlScalarNode)?.Value == endpoint.Id
                    || endpoint.Id.StartsWith(((ContractDocument.Child(node, "server") as YamlScalarNode)?.Value ?? "\0") + "/", StringComparison.Ordinal))) ?? [];
                if (RequiresAuthentication(index.Find(source)) || serverNodes.Any(RequiresAuthentication)) reason = Unsupported;
                var label = ContractJson.Redact(asset.Name + (asset.EndpointBindings.Count > 1 ? $" (binding {bindingIndex + 1})" : ""));
                yield return new(new(source, bindingIndex, label, reason is null, reason, key), request);
            }
        }
    }

    private static bool RequiresAuthentication(YamlNode? node)
    {
        if (node is YamlSequenceNode sequence) return sequence.Children.Any(RequiresAuthentication);
        if (node is not YamlMappingNode map) return false;
        foreach (var pair in map.Children)
        {
            var key = ((YamlScalarNode)pair.Key).Value;
            if (TransportRequirement(key)) return true;
            // ODCS customProperties can encode transport requirements as property/value pairs.
            if (key is "property" or "name" && ContractDocument.Child(node, "value") is not null && pair.Value is YamlScalarNode scalar &&
                TransportRequirement(scalar.Value)) return true;
            if (RequiresAuthentication(pair.Value)) return true;
        }
        return false;
    }

    private static bool TransportRequirement(string? name) => name?.Replace("-", "").Replace("_", "").ToLowerInvariant()
        is "headers" or "httpheaders" or "requestheaders" or "authentication" or "authorization"
        or "auth" or "credentials" or "security" or "apikey" or "token" or "password";

    private static string? Eligibility(DataEndpointReadRequest request, Uri baseUri)
    {
        var endpoint = request.Endpoint;
        if (endpoint.Kind is not (EndpointKind.File or EndpointKind.Api or EndpointKind.Unknown)
            || endpoint.Location is not PathLocation { Paths.Count: > 0 } paths) return Unsupported;
        try
        {
            if (paths.Paths.Any(path => !Allowed(Resolve(baseUri, path)))) return Unsupported;
            var format = new DataFormatResolver().Resolve(endpoint);
            if (format.Name is not ("csv" or "tsv" or "psv" or "xls" or "xlsx")
                && !(format.Name.Length == 0 && endpoint.Format?.Options.ContainsKey("delimiter") == true))
                return "Unsupported preview format. Use CSV, TSV, PSV or Excel.";
            if (format.Name is "xls" or "xlsx" && paths.Paths.Count != 1)
                return "Spreadsheet previews require exactly one file.";
            if (format.Compression is not (null or "gzip" or "brotli" or "gz" or "br")
                || (format.Name is "xls" or "xlsx" && format.Compression is not null))
                return "Unsupported preview compression.";
            return null;
        }
        catch (Exception) { return "Unsupported or ambiguous resource location or format."; }
    }

    private static Uri Resolve(Uri baseUri, string path) => Uri.TryCreate(path, UriKind.Absolute, out var absolute) ? absolute : new Uri(baseUri, path);
    private static bool Allowed(Uri uri) => (uri.IsFile || uri.Scheme is "http" or "https") && uri.UserInfo.Length == 0;

    public static async Task<PreviewResult> ReadAsync(DocumentSnapshot snapshot, string sourcePath, int limit,
        int? bindingIndex, CancellationToken token, IEndpointStreamResolver? streams = null, string? selectionKey = null)
    {
        PreviewResult Failure(string code, string message) => new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, limit, [], [],
            new(code, "Error", "$", message, null));
        if (limit <= 0) return Failure("PREVIEW_LIMIT", "The row limit must be a positive integer.");
        token.ThrowIfCancellationRequested();
        try
        {
            var baseUri = new Uri(snapshot.Uri, ".");
            if (EncodingSize(snapshot.Text) > ContractService.MaximumDocumentBytes)
                return Failure("PREVIEW_DOCUMENT", "The descriptor exceeds the supported document size.");
            var inspection = ContractInspector.Inspect(snapshot.Text, baseUri: baseUri, cancellationToken: token);
            var document = inspection.Document;
            var contract = inspection.Mapping.Value;
            if (contract is null) return Failure("PREVIEW_DOCUMENT", "The descriptor cannot be mapped for preview.");
            var selections = Selections(document, contract, baseUri).Where(value => value.Target.SourcePath == sourcePath).ToArray();
            if (bindingIndex is null && selections.Length > 1)
                return Failure("PREVIEW_BINDING", "Select an endpoint binding explicitly before previewing.");
            var selection = selections.SingleOrDefault(value => bindingIndex is null || value.Target.BindingIndex == bindingIndex);
            if (selection is null) return Failure("PREVIEW_SELECTION", "The selected resource or binding no longer exists. Refresh and select it again.");
            if (selectionKey is not null && selection.Target.SelectionKey != selectionKey)
                return Failure("PREVIEW_SELECTION", "The descriptor changed after selection. Refresh and select the resource again.");
            if (!selection.Target.Supported) return Failure("PREVIEW_UNSUPPORTED", selection.Target.Reason ?? Unsupported);
            var request = selection.Request!;
            var format = new DataFormatResolver().Resolve(request.Endpoint);
            var resolver = new PreviewStreams(baseUri, streams ?? new DefaultEndpointStreamResolver(),
                format.Name is "xls" or "xlsx");
            var factory = ResourceReaderFactory.Create(options => options.AddExcel(), resolver);
            using var reader = await factory.OpenAsync(request, token).ConfigureAwait(false);
            var hasRow = await NextAsync(reader, token).ConfigureAwait(false);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new PreviewColumn(reader.GetName(i),
                DeclaredType(request.Schema, reader.GetName(i))
                ?? (hasRow ? reader.GetFieldType(i)?.Name : null) ?? "unknown")).ToArray();
            if (!hasRow && columns.Length == 0 && request.Schema is { } schema)
                columns = schema.Fields.Select(field => new PreviewColumn(field.Name, field.LogicalType ?? field.PhysicalType ?? "unknown")).ToArray();
            var rows = new List<object?[]>();
            while (hasRow)
            {
                token.ThrowIfCancellationRequested();
                rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray());
                if (rows.Count == limit) break;
                hasRow = await NextAsync(reader, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, limit, columns, rows);
        }
        catch (OperationCanceledException) { throw; }
        catch (NotSupportedException) { return Failure("PREVIEW_FORMAT", "The resource format or layout is unsupported for preview."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException)
        { return Failure("PREVIEW_ACCESS", "Unable to access the resource. Check its location, permissions and public or pre-signed URL validity."); }
        catch (Exception) { return Failure("PREVIEW_PARSE", "Unable to parse the resource. Check its format, dialect and declared types."); }
    }

    private static async ValueTask<bool> NextAsync(IDataReader reader, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = reader is DbDataReader asyncReader ? await asyncReader.ReadAsync(token).ConfigureAwait(false) : reader.Read();
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static int EncodingSize(string text) => System.Text.Encoding.UTF8.GetByteCount(text);
    private static string? DeclaredType(DataSchema? schema, string name)
    {
        var field = schema?.Fields.FirstOrDefault(value => value.Name == name);
        return field?.LogicalType ?? field?.PhysicalType;
    }

    private sealed class PreviewStreams(Uri baseUri, IEndpointStreamResolver inner, bool seekable) : IEndpointStreamResolver
    {
        public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            var uri = Resolve(baseUri, path);
            if (!Allowed(uri)) throw new NotSupportedException();
            var stream = await inner.OpenAsync(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            if (seekable) return await SeekableStaging.OpenOwnedAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            // Make synchronous readers observe cancellation while blocked in resource reads too.
            return new BoundedReadStream(stream, long.MaxValue, cancellationToken);
        }
    }
}
