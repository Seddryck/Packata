using Packata.Core.Contracts;
namespace Packata.Application.Generation;

/// <summary>Loads only explicitly referenced local contract metadata, never resource records or connectors.</summary>
internal sealed class DdlReferences(ContractDocument document, DataContract contract, DataAsset[] selected,
    List<GenerationDiagnostic> diagnostics, CancellationToken token)
{
    internal sealed record Resolution(DataContract Contract, DataAsset[] Assets, DataAsset[] References);
    private readonly Dictionary<string, DataContract> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataAsset> references = new(StringComparer.Ordinal);
    private readonly List<DataEndpoint> endpoints = [.. contract.Endpoints];

    internal async Task<Resolution> ResolveAsync()
    {
        var assets = new List<DataAsset>();
        foreach (var asset in selected)
        {
            var relationships = new List<DataRelationship>();
            foreach (var relationship in asset.Schema!.Relationships)
            {
                token.ThrowIfCancellationRequested();
                var target = relationship.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)
                    ? await ResolveTargetAsync(asset, relationship).ConfigureAwait(false) : null;
                relationships.Add(target is null ? relationship : relationship with { TargetAsset = target.Id });
            }
            assets.Add(asset with { Schema = asset.Schema with { Relationships = relationships.ToArray() } });
        }
        return new(contract with { Endpoints = endpoints.ToArray() }, assets.ToArray(), references.Values.ToArray());
    }

    private async Task<DataAsset?> ResolveTargetAsync(DataAsset source, DataRelationship relationship)
    {
        var marker = relationship.TargetAsset.IndexOf("#/schema/", StringComparison.Ordinal);
        if (marker < 0) return ResolveLocal(relationship.TargetAsset);
        var uri = ResolveUri(source, relationship, relationship.TargetAsset[..marker]);
        if (uri is null) return null;
        var external = await LoadAsync(source, relationship, uri).ConfigureAwait(false);
        return external is null ? null : ResolveExternal(source, relationship, uri, external);
    }

    private DataAsset? ResolveLocal(string target)
    {
        var local = contract.Assets.Where(a => a.Id == target || a.Name == target).ToArray();
        if (local.Length != 1 || local[0].Schema is not { Fields.Count: > 0 }) return null;
        if (!selected.Any(a => a.Id == local[0].Id)) references.TryAdd(local[0].Id, local[0]);
        return local[0];
    }

    private DataAsset? ResolveExternal(DataAsset source, DataRelationship relationship, Uri uri, DataContract external)
    {
        var marker = relationship.TargetAsset.IndexOf("#/schema/", StringComparison.Ordinal);
        var name = Uri.UnescapeDataString(relationship.TargetAsset[(marker + "#/schema/".Length)..]);
        var matches = external.Assets.Where(a => a.Id == name || a.Name == name).ToArray();
        if (matches.Length != 1 || matches[0].Schema is not { Fields.Count: > 0 })
        {
            Failure(source, relationship, "does not identify one table with a usable schema in the referenced contract");
            return null;
        }
        var target = Import(matches[0], uri);
        references.TryAdd(target.Id, target);
        return references[target.Id];
    }

    private Uri? ResolveUri(DataAsset source, DataRelationship relationship, string locator)
    {
        if (document.BaseUri is not null && Uri.TryCreate(document.BaseUri, locator, out var uri)
            && uri.IsFile && !uri.IsUnc && string.IsNullOrEmpty(uri.Query)) return uri;
        Failure(source, relationship, "requires a local contract descriptor resolved relative to the source document; remote references are not fetched");
        return null;
    }

    private async Task<DataContract?> LoadAsync(DataAsset source, DataRelationship relationship, Uri uri)
    {
        if (documents.TryGetValue(uri.AbsoluteUri, out var cached)) return cached;
        try
        {
            var text = await File.ReadAllTextAsync(uri.LocalPath, token).ConfigureAwait(false);
            var mapping = ContractDocument.Parse(text, baseUri: new Uri(uri, ".")).Map();
            if (!mapping.IsSuccessful) throw new FormatException();
            var mapped = mapping.RequireValue();
            documents.Add(uri.AbsoluteUri, mapped);
            endpoints.AddRange(mapped.Endpoints.Select(e => e with { Id = EndpointId(uri, e.Id) }));
            return mapped;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            Failure(source, relationship, "could not read or map the referenced contract descriptor");
            return null;
        }
    }

    private static DataAsset Import(DataAsset asset, Uri uri) => asset with
    {
        Id = uri.AbsoluteUri + "#/schema/" + Uri.EscapeDataString(asset.Id),
        EndpointBindings = asset.EndpointBindings.Select(b => b with { EndpointId = EndpointId(uri, b.EndpointId) }).ToArray()
    };

    private static string EndpointId(Uri uri, string id) => uri.AbsoluteUri + "#endpoint/" + Uri.EscapeDataString(id);

    private void Failure(DataAsset source, DataRelationship relationship, string reason)
        => diagnostics.Add(new("PROV003", "Error", "$.assets/" + source.Id,
            ContractJson.Redact($"Relationship to '{relationship.TargetAsset}' {reason}.")));
}
