using Packata.Application;
using Packata.Application.Validation;

namespace Packata.LanguageServer.Core;

public static class SelectedValidation
{
    private const string SchemaId = "schema";
    private const string DataPackageFormat = "datapackage";
    public static readonly IReadOnlyList<ValidatorOption> Validators = Array.AsReadOnly(new[]
    {
        new ValidatorOption(SchemaId, "Schema", "Check descriptor structure, required properties and types; not dataset records.", ["odcs", DataPackageFormat], []),
        new ValidatorOption("semantic", "Semantic", "Check consistency, references and supported contract rules. Blocked if selected schema checks fail.", ["odcs", DataPackageFormat], [SchemaId])
    });

    public static ValidationOptions Options(DocumentSnapshot snapshot) => new(1, Format(snapshot), Validators);

    public static ValidationResult Validate(DocumentSnapshot snapshot, IReadOnlyList<string>? selected = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        selected ??= [SchemaId, "semantic"];
        if (!ValidSelection(selected))
            throw new ArgumentException("Select at least one available validation check.", nameof(selected));
        var layers = ValidationLayers.None;
        if (selected.Contains(SchemaId)) layers |= ValidationLayers.Schema;
        if (selected.Contains("semantic")) layers |= ValidationLayers.Semantic;
        if (TooLarge(snapshot.Text))
            return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, false, SyntaxDiagnostics(snapshot.Text, cancellationToken),
                Validators.Select(option => new ValidationCheck(option.Id, option.Label, selected.Contains(option.Id) ? "Blocked" : "NotSelected",
                    Reason: "Document exceeds the 4 MiB size limit.")).ToArray(), Format(snapshot));
        var report = DescriptorValidation.Validate(snapshot.Text, "auto", layers, new Uri(snapshot.Uri, "."), cancellationToken);
        var diagnostics = Diagnostics(report);
        var checks = Validators.Select(option => Check(option, selected, report)).ToArray();
        var format = report.Format ?? DescriptorFormat.Recover(snapshot.Text, snapshot.Uri);
        return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, report.Valid, diagnostics, checks, format, report.Profile);
    }

    private static bool ValidSelection(IReadOnlyList<string> selected)
        => selected.Count > 0 && selected.Distinct().Count() == selected.Count && selected.All(id => Validators.Any(option => option.Id == id));

    private static DocumentDiagnostic[] Diagnostics(DescriptorValidationReport report)
    {
        if (report.SyntaxDiagnostics.Count > 0) return ProjectSyntax(report.SyntaxDiagnostics);
        var index = report.Syntax is null ? null : new SourceIndex(report.Syntax.Root);
        var diagnostics = report.Diagnostics.Select(finding => new DocumentDiagnostic(finding.Code,
            finding.Severity.ToString(), ContractJson.Redact(finding.SourcePath), ContractJson.Redact(finding.Message),
            FindingRange(index, finding.SourcePath), finding.Code == "VAL_PARSE" ? "Syntax" : finding.Layer.ToString())).ToArray();
        return diagnostics.Distinct().ToArray();
    }

    public static DocumentDiagnostic[] SyntaxDiagnostics(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TooLarge(text)) return [new("VAL_SIZE", "Error", "$", "Document exceeds the 4 MiB size limit; checks are blocked.", null, "Syntax")];
        return ProjectSyntax(DescriptorSyntaxValidation.Validate(text, cancellationToken).Diagnostics);
    }

    private static DocumentDiagnostic[] ProjectSyntax(IReadOnlyList<SyntaxDiagnostic> diagnostics) => diagnostics.Select(value =>
        {
            var position = value.Location is { } location ? new Position(location.Line - 1, location.Column - 1) : null;
            return new DocumentDiagnostic(value.Code, value.Severity.ToString(), value.SourcePath, value.Message,
                position is null ? null : new SourceRange(position, position), "Syntax");
        }).ToArray();

    private static ValidationCheck Check(ValidatorOption option, IReadOnlyList<string> selected, DescriptorValidationReport report)
    {
        if (!selected.Contains(option.Id)) return new(option.Id, option.Label, "NotSelected");
        var layer = option.Id == SchemaId ? ValidationLayers.Schema : ValidationLayers.Semantic;
        var outcome = report.Layers.Single(result => result.Layer == layer);
        var errors = report.Diagnostics.Count(d => d.Layer == layer && d.Severity == Packata.Core.Contracts.MappingSeverity.Error);
        var state = outcome.Prerequisites == ValidationPrerequisiteStatus.Failed ? "Blocked" : outcome.State.ToString();
        return new(option.Id, option.Label, state, errors, outcome.Reason);
    }

    private static SourceRange? FindingRange(SourceIndex? index, string path)
    {
        if (index is null) return null;
        while (path != "$")
        {
            if (index.DiagnosticRange(path) is { } range) return range;
            var separator = path.LastIndexOf('/');
            if (separator <= 0) break;
            path = path[..separator];
        }
        return null;
    }

    private static bool TooLarge(string text) => System.Text.Encoding.UTF8.GetByteCount(text) > ContractService.MaximumDocumentBytes;

    private static string Format(DocumentSnapshot snapshot) => TooLarge(snapshot.Text)
        ? DescriptorFormat.Recover(snapshot.Text, snapshot.Uri)
        : DescriptorFormat.Detect(snapshot.Text, snapshot.Uri);
}
