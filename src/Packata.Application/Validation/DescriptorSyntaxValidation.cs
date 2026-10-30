using System.Text.Json;
using System.Text.Json.Nodes;
using Packata.Core.Contracts;
using YamlDotNet.Core;

namespace Packata.Application.Validation;

/// <summary>One-based parser coordinates, independent of an editor's range representation.</summary>
public sealed record SyntaxLocation(int Line, int Column);
public sealed record SyntaxDiagnostic(string Code, MappingSeverity Severity, string SourcePath, string Message, SyntaxLocation? Location = null);
public sealed record SyntaxValidationResult(ContractSyntax? Syntax, IReadOnlyList<SyntaxDiagnostic> Diagnostics)
{
    public bool Valid => Syntax is not null && Diagnostics.Count == 0;
    // Reuse the strict JSON parse during layered validation without exposing a mutable JSON tree.
    internal JsonObject? JsonRoot { get; init; }
}

/// <summary>Checks bounded YAML mapping syntax and strict JSON syntax without native, schema or semantic validation.</summary>
public static class DescriptorSyntaxValidation
{
    public static SyntaxValidationResult Validate(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContractSyntax? syntax = null;
        try
        {
            syntax = ContractSyntax.Parse(text);
            var json = text.TrimStart().StartsWith('{') ? JsonNode.Parse(text) as JsonObject
                ?? throw new FormatException("A mapping is required.") : null;
            cancellationToken.ThrowIfCancellationRequested();
            return new(syntax, []) { JsonRoot = json };
        }
        catch (Exception error) when (error is YamlException or FormatException or ArgumentException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = error is YamlException yaml && yaml.Start.Line > 0
                ? new SyntaxLocation((int)yaml.Start.Line, (int)Math.Max(1, yaml.Start.Column)) : null;
            return new(syntax, [new("VAL_PARSE", MappingSeverity.Error, "$",
                "Invalid YAML/JSON syntax; validation checks are blocked.", location)]);
        }
    }
}
