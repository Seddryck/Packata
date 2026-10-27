using System.Collections.Concurrent;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Packata.LanguageServer.Core;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Packata.LanguageServer.Handlers;

public sealed class DiagnosticPublisher(DocumentStore documents, ILanguageServerFacade server)
{
    public void Publish(string uri, int? version, IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        if (!documents.IsCurrent(new(new Uri(uri), "", version))) return;
        server.TextDocument.PublishDiagnostics(new()
        {
            Uri = DocumentUri.From(uri), Version = version,
            Diagnostics = diagnostics.Select(value => new Diagnostic
            {
                Code = value.Code, Source = "Packata", Message = value.Message,
                Severity = value.Severity switch { "Error" => DiagnosticSeverity.Error, "Warning" => DiagnosticSeverity.Warning, _ => DiagnosticSeverity.Information },
                Range = value.Range is { } range ? new LspRange(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character) : new LspRange(0, 0, 0, 0),
                Data = new Newtonsoft.Json.Linq.JObject { ["sourcePath"] = value.SourcePath, ["locationUnavailable"] = value.Range is null }
            }).ToArray()
        });
    }
}

public sealed class DocumentSyncHandler(DocumentStore documents, ContractService contracts, DiagnosticPublisher publisher)
    : TextDocumentSyncHandlerBase, IDisposable
{
    private readonly ConcurrentDictionary<Uri, CancellationTokenSource> pending = new();
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "packata");
    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        Changed(request.TextDocument.Uri.ToUri(), request.TextDocument.Text, request.TextDocument.Version ?? 0);
        return Unit.Task;
    }
    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        if (request.ContentChanges.LastOrDefault()?.Text is { } text)
            Changed(request.TextDocument.Uri.ToUri(), text, request.TextDocument.Version ?? 0);
        return Unit.Task;
    }
    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = WorkspaceUri.Parse(request.TextDocument.Uri.ToString());
        if (pending.TryRemove(uri, out var work)) Cancel(work);
        documents.Close(uri);
        publisher.Publish(uri.AbsoluteUri, null, []);
        return Unit.Task;
    }
    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) => Unit.Task;
    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Scheme = "file", Pattern = "**/*.{json,yaml,yml}" }),
            Change = TextDocumentSyncKind.Full, Save = false
        };

    private void Changed(Uri uri, string text, int version)
    {
        uri = WorkspaceUri.Parse(uri.AbsoluteUri);
        if (!documents.Update(uri, text, version)) return;
        var work = new CancellationTokenSource();
        pending.AddOrUpdate(uri, work, (_, previous) => { Cancel(previous); return work; });
        _ = ValidateLaterAsync(uri, version, work);
    }
    private async Task ValidateLaterAsync(Uri uri, int version, CancellationTokenSource work)
    {
        try
        {
            await Task.Delay(180, work.Token);
            var result = await contracts.ValidateAsync(uri, work.Token);
            work.Token.ThrowIfCancellationRequested();
            if (result.Version == version) publisher.Publish(result.Uri, version, result.Diagnostics);
        }
        catch (OperationCanceledException) { /* Superseded edits and closed documents intentionally cancel their pending analysis. */ }
        catch (Exception) { /* Never log parser/source exception details. Explicit requests report failures. */ }
        finally
        {
            ((ICollection<KeyValuePair<Uri, CancellationTokenSource>>)pending).Remove(new(uri, work));
            work.Dispose();
        }
    }
    public void Dispose()
    {
        foreach (var work in pending.Values) Cancel(work);
        pending.Clear();
    }
    private static void Cancel(CancellationTokenSource work)
    {
        try { work.Cancel(); }
        catch (ObjectDisposedException) { /* The completed analysis already removed and released its token source. */ }
    }
}
