using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal sealed record CategoryDecodingOptions(bool Enabled = false, int RowLimit = 100_000,
    long ByteLimit = 16 * 1024 * 1024, string? Language = null, string? Label = null, Action<string>? Diagnostic = null)
{
    public static CategoryDecodingOptions From(ResourceReaderFactoryOptions options)
    {
        ValidateSelectors(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CategoryTableRowLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CategoryTableByteLimit);
        var enabled = options.DecodeCategories || options.DecodeCategoriesLanguage is not null || options.DecodeCategoriesLabel is not null;
        return new(enabled, options.CategoryTableRowLimit, options.CategoryTableByteLimit,
            options.DecodeCategoriesLanguage, options.DecodeCategoriesLabel, options.CategoryDecodingDiagnostic);
    }

    private static void ValidateSelectors(ResourceReaderFactoryOptions options)
    {
        if (options.DecodeCategoriesLanguage is not null && options.DecodeCategoriesLabel is not null)
            throw new ArgumentException("DecodeCategoriesLanguage and DecodeCategoriesLabel are mutually exclusive.");
        if (options.DecodeCategoriesLanguage is not null && !LanguageTag.IsValid(options.DecodeCategoriesLanguage))
            throw new ArgumentException("DecodeCategoriesLanguage must be a language tag such as fr or fr-BE.");
        if (options.DecodeCategoriesLabel is not null) ArgumentException.ThrowIfNullOrWhiteSpace(options.DecodeCategoriesLabel);
    }
}
