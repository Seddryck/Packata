using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Packata.LanguageServer.Core;
using Newtonsoft.Json.Linq;

namespace Packata.LanguageServer.Handlers;

public sealed record DocumentRequest(string Uri, int SchemaVersion = 1);
public sealed record ValidationRequest(string Uri, int SchemaVersion = 1, string[]? Checks = null);
public sealed record DiscoveryRequest(string[] WorkspaceFolders, int SchemaVersion = 1);
public sealed record SchemaGenerationOptionsRequest(string Uri, string? SelectionKey = null, int SchemaVersion = 1);

public sealed class SchemaDocumentOptionsHandler(SchemaDocumentOperations documents, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<DocumentRequest, SchemaDocumentDiscovery>("packata.schema.document.options", serializer)
{
    public override Task<SchemaDocumentDiscovery> Handle(DocumentRequest request, CancellationToken token)
        => ProtocolVersion.RunAsync(request.SchemaVersion, () => documents.OptionsAsync(WorkspaceUri.Parse(request.Uri), token));
}

public sealed class SchemaDocumentHandler(SchemaDocumentOperations documents, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<SchemaDocumentRequest, SchemaDocumentResponse>("packata.schema.document.export", serializer)
{
    public override Task<SchemaDocumentResponse> Handle(SchemaDocumentRequest request, CancellationToken token)
        => ProtocolVersion.RunAsync(request.SchemaVersion, () => documents.ExportAsync(request, token));
}

public sealed class SchemaGenerationOptionsHandler(SchemaGenerationOperations generation, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<SchemaGenerationOptionsRequest, GenerationOptionsResult>("packata.schema.export.options", serializer)
{
    public override Task<GenerationOptionsResult> Handle(SchemaGenerationOptionsRequest request, CancellationToken cancellationToken)
        => ProtocolVersion.RunAsync(request.SchemaVersion, () => generation.OptionsAsync(WorkspaceUri.Parse(request.Uri), request.SelectionKey, cancellationToken));
}

public sealed class SchemaGenerationHandler(SchemaGenerationOperations generation, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<SchemaGenerationRequest, SchemaGenerationResponse>("packata.schema.export", serializer)
{
    public override Task<SchemaGenerationResponse> Handle(SchemaGenerationRequest request, CancellationToken cancellationToken)
        => ProtocolVersion.RunAsync(request.SchemaVersion, () => generation.GenerateAsync(request, cancellationToken));
}
public sealed record PreviewRequest(string Uri, string SourcePath, JToken? Limit = null, int? BindingIndex = null,
    string? SelectionKey = null, int SchemaVersion = 1);

public sealed class PreviewHandler(ContractService contracts, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<PreviewRequest, PreviewResult>("packata.resource.preview", serializer)
{
    public override Task<PreviewResult> Handle(PreviewRequest request, CancellationToken cancellationToken)
    {
        // JSON.NET may coerce fractional numbers or strings into integer properties. Require an
        // actual integer token so invalid limits cannot accidentally initiate resource access.
        var limit = request.Limit is null ? ResourcePreview.DefaultLimit
            : request.Limit.Type == JTokenType.Integer && int.TryParse(request.Limit.ToString(), out var value) ? value : 0;
        return ProtocolVersion.RunAsync(request.SchemaVersion, () => contracts.PreviewAsync(WorkspaceUri.Parse(request.Uri),
            request.SourcePath, cancellationToken, limit, request.BindingIndex, request.SelectionKey));
    }
}

internal static class ProtocolVersion
{
    public static async Task<T> RunAsync<T>(int version, Func<Task<T>> operation)
    {
        if (version != ContractService.SchemaVersion)
            throw new RpcErrorException(-32602, new object(), "Unsupported Packata payload version. Update the client and server together.");
        try { return await operation(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // The default JSON-RPC exception formatter includes exception details and source paths.
            throw new RpcErrorException(-32001, new object(), "Unable to read or process the requested workspace document. Check its URI and access permissions.");
        }
    }
}

public sealed class InspectHandler(ContractService contracts, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<DocumentRequest, Inspection>("packata.contract.inspect", serializer)
{
    public override Task<Inspection> Handle(DocumentRequest request, CancellationToken cancellationToken)
    {
        return ProtocolVersion.RunAsync(request.SchemaVersion, () => contracts.InspectAsync(WorkspaceUri.Parse(request.Uri), cancellationToken));
    }
}

public sealed class ValidateHandler(ContractService contracts, DiagnosticPublisher publisher, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<ValidationRequest, ValidationResult>("packata.contract.validate", serializer)
{
    public override async Task<ValidationResult> Handle(ValidationRequest request, CancellationToken cancellationToken)
    {
        var result = await ProtocolVersion.RunAsync(request.SchemaVersion, () => contracts.ValidateAsync(WorkspaceUri.Parse(request.Uri), cancellationToken, request.Checks));
        cancellationToken.ThrowIfCancellationRequested();
        publisher.Publish(result.Uri, result.Version, result.Diagnostics, explicitValidation: true);
        return result;
    }
}

public sealed class ValidationOptionsHandler(ContractService contracts, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<DocumentRequest, ValidationOptions>("packata.validation.options", serializer)
{
    public override Task<ValidationOptions> Handle(DocumentRequest request, CancellationToken cancellationToken)
        => ProtocolVersion.RunAsync(request.SchemaVersion, () => contracts.ValidationOptionsAsync(WorkspaceUri.Parse(request.Uri), cancellationToken));
}

public sealed class DiscoverHandler(ContractService contracts, ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<DiscoveryRequest, DiscoveryResult>("packata.workspace.discover", serializer)
{
    public override Task<DiscoveryResult> Handle(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        return ProtocolVersion.RunAsync(request.SchemaVersion, () => contracts.DiscoverAsync(request.WorkspaceFolders, cancellationToken));
    }
}

// A versioned advertised command lets clients negotiate Packata payloads independently of LSP itself.
public sealed class ProtocolHandler(ISerializer serializer)
    : ExecuteTypedResponseCommandHandlerBase<DiscoveryRequest, object>("packata.protocol.v1", serializer)
{
    public override Task<object> Handle(DiscoveryRequest request, CancellationToken cancellationToken)
        => ProtocolVersion.RunAsync<object>(request.SchemaVersion, () => Task.FromResult<object>(new { schemaVersion = 1, schemaVersions = new[] { 1 }, positionEncoding = "utf-16" }));
}
