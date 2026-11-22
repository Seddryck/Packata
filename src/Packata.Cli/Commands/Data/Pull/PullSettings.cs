using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class PullSettings : CommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("HTTP(S) URL of a JSON or YAML Data Package descriptor.")]
    public string Source { get; init; } = string.Empty;

    [CommandOption("--output-dir <DIRECTORY>")]
    [Description("Destination directory. Defaults to the current directory.")]
    public string OutputDirectory { get; init; } = ".";

    public override ValidationResult Validate()
    {
        if (!Uri.TryCreate(Source, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https"))
            return ValidationResult.Error("SOURCE must be an HTTP(S) Data Package descriptor URL.");
        return string.IsNullOrWhiteSpace(OutputDirectory)
            ? ValidationResult.Error("--output-dir must specify a directory.")
            : ValidationResult.Success();
    }
}
