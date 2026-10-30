using Packata.Documents;
using Packata.Documents.Validation;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

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

    public static ValidationResult Validate(DocumentSnapshot snapshot, IReadOnlyList<string>? selected = null)
    {
        selected ??= [SchemaId, "semantic"];
        if (!ValidSelection(selected))
            throw new ArgumentException("Select at least one available validation check.", nameof(selected));
        var format = Format(snapshot);
        var layers = ValidationLayers.None;
        if (selected.Contains(SchemaId)) layers |= ValidationLayers.Schema;
        if (selected.Contains("semantic")) layers |= ValidationLayers.Semantic;
        if (TooLarge(snapshot.Text))
            return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, false, SyntaxDiagnostics(snapshot.Text),
                Validators.Select(option => new ValidationCheck(option.Id, option.Label, selected.Contains(option.Id) ? "Blocked" : "NotSelected",
                    Reason: "Document exceeds the 4 MiB size limit.")).ToArray(), format);
        var report = DescriptorValidation.Validate(snapshot.Text, "auto", layers, new Uri(snapshot.Uri, "."));
        var diagnostics = Diagnostics(snapshot.Text, report);
        var checks = Validators.Select(option => Check(option, selected, report)).ToArray();
        return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, report.Valid, diagnostics, checks, format, report.Profile);
    }

    private static bool ValidSelection(IReadOnlyList<string> selected)
        => selected.Count > 0 && selected.Distinct().Count() == selected.Count && selected.All(id => Validators.Any(option => option.Id == id));

    private static DocumentDiagnostic[] Diagnostics(string text, DescriptorValidationReport report)
    {
        var index = Index(text);
        var diagnostics = report.Diagnostics.Select(finding => new DocumentDiagnostic(finding.Code,
            finding.Severity.ToString(), ContractJson.Redact(finding.SourcePath), ContractJson.Redact(finding.Message),
            FindingRange(index, finding.SourcePath), finding.Code == "VAL_PARSE" ? "Syntax" : finding.Layer.ToString())).ToArray();
        if (report.Diagnostics.Any(d => d.Code == "VAL_PARSE") && SyntaxDiagnostics(text) is { Length: > 0 } syntax) diagnostics = syntax;
        return diagnostics.Distinct().ToArray();
    }

    public static DocumentDiagnostic[] SyntaxDiagnostics(string text)
    {
        if (TooLarge(text)) return [new("VAL_SIZE", "Error", "$", "Document exceeds the 4 MiB size limit; checks are blocked.", null, "Syntax")];
        try
        {
            ContractDocument.ParseRoot(text);
            if (text.TrimStart().StartsWith('{')) System.Text.Json.JsonDocument.Parse(text).Dispose();
            return [];
        }
        catch (Exception error) when (error is YamlException or FormatException or ArgumentException or System.Text.Json.JsonException)
        {
            SourceRange? range = null;
            if (error is YamlException yaml && yaml.Start.Line > 0)
            {
                var position = new Position((int)yaml.Start.Line - 1, (int)Math.Max(0, yaml.Start.Column - 1));
                range = new(position, position);
            }
            return [new("VAL_PARSE", "Error", "$", "Invalid YAML/JSON syntax; validation checks are blocked.", range, "Syntax")];
        }
    }

    private static ValidationCheck Check(ValidatorOption option, IReadOnlyList<string> selected, DescriptorValidationReport report)
    {
        if (!selected.Contains(option.Id)) return new(option.Id, option.Label, "NotSelected");
        var layer = option.Id == SchemaId ? ValidationLayers.Schema : ValidationLayers.Semantic;
        var outcome = report.Layers.Single(result => result.Layer == layer);
        var errors = report.Diagnostics.Count(d => d.Layer == layer && d.Severity == Packata.Core.Contracts.MappingSeverity.Error);
        var prerequisiteFailed = report.Diagnostics.Any(d => d.Layer == layer && d.Code is "VAL_PREREQUISITE" or "SEM_STRUCTURE");
        var state = outcome.State == ValidationState.Skipped || prerequisiteFailed ? "Blocked" : outcome.State.ToString();
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

    private static SourceIndex? Index(string text)
    {
        if (TooLarge(text)) return null;
        try { return new(ContractDocument.ParseRoot(text)); }
        catch (Exception error) when (error is YamlException or FormatException or ArgumentException) { return null; }
    }

    private static bool TooLarge(string text) => System.Text.Encoding.UTF8.GetByteCount(text) > ContractService.MaximumDocumentBytes;

    private static string Format(DocumentSnapshot snapshot)
    {
        var index = Index(snapshot.Text);
        if (index is not null)
            return index.Find("/kind") is not null || index.Find("/apiVersion") is not null ? "odcs" : DataPackageFormat;
        return snapshot.Uri.LocalPath.Contains("odcs", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(snapshot.Uri.LocalPath).StartsWith("contract", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Text.Contains("apiVersion", StringComparison.Ordinal) ? "odcs" : DataPackageFormat;
    }
}
