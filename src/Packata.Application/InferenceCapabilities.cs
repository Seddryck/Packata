namespace Packata.Application;

/// <summary>Semantic options, including explicitly supplied values equal to their defaults.</summary>
[Flags]
public enum InferenceOptions
{
    None = 0, MaximumRecords = 1, MaximumBytes = 2, Culture = 4, Header = 8,
    DialectInference = 16, SheetName = 32, SheetNumber = 64
}

public sealed class UnsupportedInferenceOptionsException(string format, InferenceOptions options)
    : NotSupportedException($"Inference options {options} do not apply to the '{format}' resource format.")
{
    public string Format { get; } = format;
    public InferenceOptions Options { get; } = options;
}

public static class InferenceCapabilities
{
    public const InferenceOptions AllOptions = InferenceOptions.MaximumRecords | InferenceOptions.MaximumBytes
        | InferenceOptions.Culture | InferenceOptions.Header | InferenceOptions.DialectInference
        | InferenceOptions.SheetName | InferenceOptions.SheetNumber;

    public static void Validate(string format, ContractInferenceOptions options)
    {
        options.Validate();
        var unsupported = Supplied(options) & ~Allowed(format);
        if (unsupported != InferenceOptions.None) throw new UnsupportedInferenceOptionsException(format, unsupported);
    }

    private static InferenceOptions Supplied(ContractInferenceOptions options)
    {
        var supplied = options.SuppliedOptions;
        if (options.MaximumRecords != 1000) supplied |= InferenceOptions.MaximumRecords;
        if (options.MaximumBytes is not null) supplied |= InferenceOptions.MaximumBytes;
        if (!string.IsNullOrWhiteSpace(options.Culture)) supplied |= InferenceOptions.Culture;
        if (!options.Header.Equals("auto", StringComparison.OrdinalIgnoreCase)) supplied |= InferenceOptions.Header;
        if (options.NoDialectInference) supplied |= InferenceOptions.DialectInference;
        if (options.SheetName is not null) supplied |= InferenceOptions.SheetName;
        if (options.SheetNumber is not null) supplied |= InferenceOptions.SheetNumber;
        return supplied;
    }

    private static InferenceOptions Allowed(string format) => format switch
        {
            "xls" or "xlsx" => InferenceOptions.MaximumRecords | InferenceOptions.MaximumBytes | InferenceOptions.Header
                | InferenceOptions.SheetName | InferenceOptions.SheetNumber,
            "parquet" or "pqt" => InferenceOptions.None,
            _ => InferenceOptions.MaximumRecords | InferenceOptions.MaximumBytes | InferenceOptions.Culture
                | InferenceOptions.Header | InferenceOptions.DialectInference
        };
}
