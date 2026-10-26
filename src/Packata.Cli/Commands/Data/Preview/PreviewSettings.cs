using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class PreviewSettings : StagingCommandSettings
{
    [CommandOption("--decode-categories")]
    [Description("Decode inline and local category-table codes to labels (PocketCsvReader CSV/TSV/PSV only; default: disabled).")]
    public bool DecodeCategories { get; init; }

    [CommandOption("--decode-categories-language <LANGUAGE>")]
    [Description("Decode category-table labels in a language such as fr or fr-BE; enables decoding.")]
    public string? DecodeCategoriesLanguage { get; init; }

    [CommandOption("--decode-categories-label <FIELD>")]
    [Description("Decode using an exact declared category-table label field; enables decoding.")]
    public string? DecodeCategoriesLabel { get; init; }

    [CommandOption("--asset <ID>")]
    [Description(OptionValidation.AssetDescription)]
    public required string Asset { get; init; }

    [CommandOption("--limit <COUNT>")]
    [Description("Maximum data rows to return, excluding headers. Must be positive.")]
    [DefaultValue(20)]
    [TypeConverter(typeof(PreviewLimitConverter))]
    public int Limit { get; init; } = 20;

    [CommandOption("--columns <NAMES>")]
    [Description("Comma-separated exact field names in the requested order; default is all fields.")]
    public string? Columns { get; init; }

    [CommandOption("--output-format <FORMAT>")]
    [Description("Output representation: table, json, or csv.")]
    [DefaultValue("table")]
    public string OutputFormat { get; init; } = "table";

    [CommandOption("-o|--output <PATH>")]
    [Description("Write the preview to a new file instead of stdout.")]
    public string? Output { get; init; }

    public override ValidationResult Validate()
    {
        var source = base.Validate();
        if (!source.Successful) return source;
        var decoding = ValidateDecoding();
        if (!decoding.Successful) return decoding;
        if (string.IsNullOrWhiteSpace(Asset)) return ValidationResult.Error("--asset is required.");
        if (Limit <= 0) return ValidationResult.Error("--limit must be a positive integer.");
        if (OutputFormat is not ("table" or "json" or "csv")) return ValidationResult.Error("--output-format must be table, json, or csv.");
        var columns = ValidateColumns();
        return columns.Successful ? OptionValidation.Output(Output) : columns;
    }

    private ValidationResult ValidateDecoding()
    {
        if (DecodeCategoriesLanguage is not null && DecodeCategoriesLabel is not null)
            return ValidationResult.Error("--decode-categories-language and --decode-categories-label are mutually exclusive.");
        if (DecodeCategoriesLanguage is not null && !Packata.Core.Contracts.LanguageTag.IsValid(DecodeCategoriesLanguage))
            return ValidationResult.Error("--decode-categories-language must be a language tag such as fr or fr-BE.");
        if (DecodeCategoriesLabel is not null && string.IsNullOrWhiteSpace(DecodeCategoriesLabel))
            return ValidationResult.Error("--decode-categories-label must name a declared label field.");
        return ValidationResult.Success();
    }

    private ValidationResult ValidateColumns()
    {
        if (Columns is not null)
        {
            var names = Columns.Split(',', StringSplitOptions.TrimEntries);
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                return ValidationResult.Error("--columns must contain non-empty, unique comma-separated field names.");
        }
        return ValidationResult.Success();
    }
}
