namespace Packata.Application;

/// <summary>Semantic options, including explicitly supplied values equal to their defaults.</summary>
[Flags]
public enum InferenceOption
{
    None = 0, MaximumRecords = 1, MaximumBytes = 2, Culture = 4, Header = 8,
    DialectInference = 16, SheetName = 32, SheetNumber = 64
}

public sealed class UnsupportedInferenceOptionsException(string format, InferenceOption options)
    : NotSupportedException($"Inference options {options} do not apply to the '{format}' resource format.")
{
    public string Format { get; } = format;
    public InferenceOption Options { get; } = options;
}

public static class InferenceCapabilities
{
    public const InferenceOption AllOptions = InferenceOption.MaximumRecords | InferenceOption.MaximumBytes
        | InferenceOption.Culture | InferenceOption.Header | InferenceOption.DialectInference
        | InferenceOption.SheetName | InferenceOption.SheetNumber;

    public static void Validate(string format, ContractInferenceOptions options)
    {
        options.Validate();
        var supplied = options.SuppliedOptions;
        if (options.MaximumRecords != 1000) supplied |= InferenceOption.MaximumRecords;
        if (options.MaximumBytes is not null) supplied |= InferenceOption.MaximumBytes;
        if (!string.IsNullOrWhiteSpace(options.Culture)) supplied |= InferenceOption.Culture;
        if (!options.Header.Equals("auto", StringComparison.OrdinalIgnoreCase)) supplied |= InferenceOption.Header;
        if (options.NoDialectInference) supplied |= InferenceOption.DialectInference;
        if (options.SheetName is not null) supplied |= InferenceOption.SheetName;
        if (options.SheetNumber is not null) supplied |= InferenceOption.SheetNumber;
        var allowed = format switch
        {
            "xls" or "xlsx" => InferenceOption.MaximumRecords | InferenceOption.MaximumBytes | InferenceOption.Header
                | InferenceOption.SheetName | InferenceOption.SheetNumber,
            "parquet" or "pqt" => InferenceOption.None,
            _ => InferenceOption.MaximumRecords | InferenceOption.MaximumBytes | InferenceOption.Culture
                | InferenceOption.Header | InferenceOption.DialectInference
        };
        var unsupported = supplied & ~allowed;
        if (unsupported != InferenceOption.None) throw new UnsupportedInferenceOptionsException(format, unsupported);
    }
}
