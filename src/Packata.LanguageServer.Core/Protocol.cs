using Newtonsoft.Json.Linq;

namespace Packata.LanguageServer.Core;

public sealed record Position(int Line, int Character);
public sealed record SourceRange(Position Start, Position End);
public sealed record SourceLocation(string Uri, SourceRange Range);
public sealed record DocumentDiagnostic(string Code, string Severity, string SourcePath, string Message, SourceRange? Range, string? Layer = null);
public sealed record ContractNode(string Id, string Label, string Kind, string LogicalPath,
    string? Description, SourceLocation? Location, IReadOnlyList<ContractNode> Children, string? SourcePath = null);
public sealed record Inspection(int SchemaVersion, string Uri, int? Version, string BaseUri, bool Recognized,
    string? Format, JToken? Contract, IReadOnlyList<DocumentDiagnostic> Diagnostics, ContractNode? Tree, ContractNode? NativeTree = null,
    IReadOnlyList<PreviewTarget>? PreviewTargets = null);
public sealed record ValidationResult(int SchemaVersion, string Uri, int? Version, bool Valid,
    IReadOnlyList<DocumentDiagnostic> Diagnostics, IReadOnlyList<ValidationCheck>? Checks = null, string? Format = null,
    Packata.Application.Validation.DescriptorProfile? Profile = null);
public sealed record ValidationCheck(string Id, string Label, string State, int Errors = 0, string? Reason = null);
public sealed record ValidatorOption(string Id, string Label, string Description, string[] Formats, string[] BlockedBySelected);
public sealed record ValidationOptions(int SchemaVersion, string Format, IReadOnlyList<ValidatorOption> Validators);
public sealed record DocumentSnapshot(Uri Uri, string Text, int? Version);
public sealed record DiscoveryResult(int SchemaVersion, IReadOnlyList<Inspection> Contracts);
public sealed record PreviewTarget(string SourcePath, int? BindingIndex, string Label, bool Supported, string? Reason = null, string? SelectionKey = null);
public sealed record PreviewColumn(string Name, string Type);
public sealed record PreviewResult(int SchemaVersion, string Uri, int? Version, int Limit,
    IReadOnlyList<PreviewColumn> Columns, IReadOnlyList<object?[]> Rows, DocumentDiagnostic? Diagnostic = null);
