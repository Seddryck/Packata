using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal class ContractSettings : ContractSourceSettings
{
    [CommandOption("--output-format <FORMAT>")]
    [Description("Output representation: text or json.")]
    [DefaultValue("text")]
    public string OutputFormat { get; init; } = "text";

    [CommandOption("-o|--output <PATH>")]
    [Description("Write the result to a new file instead of stdout.")]
    public string? Output { get; init; }

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        if (OutputFormat is not ("text" or "json"))
            return ValidationResult.Error("--output-format must be text or json.");
        return OptionValidation.Output(Output);
    }
}

internal sealed class ValidateSettings : ContractSettings
{
    [CommandOption("--schema")]
    [Description("Validate the original descriptor against its official JSON Schema.")]
    public bool Schema { get; init; }

    [CommandOption("--semantic")]
    [Description("Validate declaration semantics without accessing datasets or endpoints.")]
    public bool Semantic { get; init; }
}
