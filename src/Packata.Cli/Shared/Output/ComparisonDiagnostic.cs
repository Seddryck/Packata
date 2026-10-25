using Packata.Core.Contracts;

namespace Packata.Cli;

internal sealed record ComparisonDiagnostic(string Input, string Source, string Code,
    MappingSeverity Severity, string SourcePath, string Message);
