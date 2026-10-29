using Newtonsoft.Json.Linq;

namespace Packata.LanguageServer.Core;

public sealed record Position(int Line, int Character);
public sealed record SourceRange(Position Start, Position End);
public sealed record SourceLocation(string Uri, SourceRange Range);
public sealed record DocumentDiagnostic(string Code, string Severity, string SourcePath, string Message, SourceRange? Range);
public sealed record ContractNode(string Id, string Label, string Kind, string LogicalPath,
    string? Description, SourceLocation? Location, IReadOnlyList<ContractNode> Children, string? SourcePath = null);
public sealed record Inspection(int SchemaVersion, string Uri, int? Version, string BaseUri, bool Recognized,
    string? Format, JToken? Contract, IReadOnlyList<DocumentDiagnostic> Diagnostics, ContractNode? Tree, ContractNode? NativeTree = null);
public sealed record ValidationResult(int SchemaVersion, string Uri, int? Version, bool Valid,
    IReadOnlyList<DocumentDiagnostic> Diagnostics);
public sealed record DocumentSnapshot(Uri Uri, string Text, int? Version);
public sealed record DiscoveryResult(int SchemaVersion, IReadOnlyList<Inspection> Contracts);
