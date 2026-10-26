using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Handlers;

public sealed record DocumentRequest(string Uri, int SchemaVersion = 1);
public sealed record DiscoveryRequest(string[] WorkspaceFolders, int SchemaVersion = 1);

internal static class ProtocolVersion
{
    public static async Task<T> RunAsync<T>(int version, Func<Task<T>> operation)
    {
        if (version != ContractService.SchemaVersion)
            throw new RpcErrorException(-32602, null!, "Unsupported Packata payload version. Update the client and server together.");
        try { return await operation(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // The default JSON-RPC exception formatter includes exception details and source paths.
            throw new RpcErrorException(-32001, null!, "Unable to read or process the requested workspace document. Check its URI and access permissions.");
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
    : ExecuteTypedResponseCommandHandlerBase<DocumentRequest, ValidationResult>("packata.contract.validate", serializer)
{
    public override async Task<ValidationResult> Handle(DocumentRequest request, CancellationToken cancellationToken)
    {
        var result = await ProtocolVersion.RunAsync(request.SchemaVersion, () => contracts.ValidateAsync(WorkspaceUri.Parse(request.Uri), cancellationToken));
        publisher.Publish(result.Uri, result.Version, result.Diagnostics);
        return result;
    }
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
