using Spectre.Console;

namespace Packata.Cli;

internal static class OptionValidation
{
    public const string AssetDescription = "Data Package resource name or ODCS schema object name.";

    public static ValidationResult Output(string? path) => path is not null && string.IsNullOrWhiteSpace(path)
        ? ValidationResult.Error("--output must be a non-empty file path.") : ValidationResult.Success();
}
