using System.ComponentModel;
using Packata.Storages;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal abstract class StagingCommandSettings : DatabaseCommandSettings
{
    [CommandOption("--staging-maximum-bytes <COUNT>")]
    [Description("Maximum bytes staged for non-seekable Excel/Parquet data; default 268435456 (256 MiB).")]
    [DefaultValue(SeekableStaging.DefaultMaximumBytes)]
    public long StagingMaximumBytes { get; init; } = SeekableStaging.DefaultMaximumBytes;

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        return StagingMaximumBytes > 0 ? ValidationResult.Success()
            : ValidationResult.Error("--staging-maximum-bytes must be positive.");
    }
}
