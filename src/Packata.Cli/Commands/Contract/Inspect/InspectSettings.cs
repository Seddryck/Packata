using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class InspectSettings : ContractSettings
{
    [CommandOption("--asset <ID>")]
    [Description(OptionValidation.AssetDescription + " Omit to inspect all assets.")]
    public string? Asset { get; init; }
}
