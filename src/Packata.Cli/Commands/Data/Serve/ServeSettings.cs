using System.ComponentModel;
using System.Net;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ServeSettings : StagingCommandSettings
{
    [CommandOption("--asset <ID>")]
    [Description("Exact asset IDs to expose; repeat for multiple assets. At least one is required.")]
    public string[] Assets { get; init; } = [];
    [CommandOption("--binding <INDEX>")]
    [Description("Explicit zero-based source binding index (applies to each selected asset).")]
    public int? Binding { get; init; }
    [CommandOption("--address <IP>")]
    [Description("Listen IP address; default 127.0.0.1 (loopback).")]
    public string Address { get; init; } = "127.0.0.1";
    [CommandOption("--port <PORT>")]
    [Description("Listen port; 0 asks the OS to assign an available port (default).")]
    public int Port { get; init; }
    [CommandOption("--formats <LIST>")]
    [Description("Comma-separated enabled formats: ndjson,avro,arrow; default all three.")]
    public string Formats { get; init; } = "ndjson,avro,arrow";
    [CommandOption("--batch-size <COUNT>")]
    [Description("Maximum rows per flush/block/batch; default 128.")]
    public int BatchSize { get; init; } = 128;
    [CommandOption("--maximum-buffer-bytes <COUNT>")]
    [Description("Maximum logical row/block/batch buffer bytes; default 1048576 (1 MiB).")]
    public int MaximumBufferBytes { get; init; } = 1024 * 1024;

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        if (Assets.Length == 0 || Assets.Any(string.IsNullOrWhiteSpace) || Assets.Distinct().Count() != Assets.Length)
            return ValidationResult.Error("--asset must select at least one unique asset ID.");
        if (Binding is null or < 0) return ValidationResult.Error("--binding must explicitly select a zero-based binding index.");
        if (!IPAddress.TryParse(Address, out _)) return ValidationResult.Error("--address must be an IP address.");
        if (Port is < 0 or > 65535) return ValidationResult.Error("--port must be between 0 and 65535.");
        var formats = Formats.Split(',', StringSplitOptions.TrimEntries);
        if (formats.Any(format => format is not ("ndjson" or "avro" or "arrow")) || formats.Distinct().Count() != formats.Length)
            return ValidationResult.Error("--formats must contain unique names from ndjson,avro,arrow.");
        if (BatchSize <= 0 || MaximumBufferBytes <= 0) return ValidationResult.Error("Buffer limits must be positive.");
        return ValidationResult.Success();
    }
}
