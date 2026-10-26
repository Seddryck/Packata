namespace Packata.ResourceReaders;

/// <summary>Configures providers evaluated before the built-in resource readers.</summary>
public sealed class ResourceReaderFactoryOptions
{
    /// <summary>Decode inline and local category-table values to labels for the PocketCsvReader delimited engine only.
    /// Disabled by default. Columns with labels return strings, including fallback code text.
    /// Native sequences match exact unquoted CSV text; quoted codes remain unchanged.</summary>
    public bool DecodeCategories { get; set; }

    /// <summary>Select category labels by language; implies DecodeCategories. Exclusive with DecodeCategoriesLabel.</summary>
    public string? DecodeCategoriesLanguage { get; set; }

    /// <summary>Select an exact declared logical label field; implies DecodeCategories. Exclusive with DecodeCategoriesLanguage.</summary>
    public string? DecodeCategoriesLabel { get; set; }

    /// <summary>Receives one fallback diagnostic per affected category table per reader opening.</summary>
    public Action<string>? CategoryDecodingDiagnostic { get; set; }

    /// <summary>Maximum total category-table rows loaded per reader opening. Defaults to 100,000.</summary>
    public int CategoryTableRowLimit { get; set; } = 100_000;

    /// <summary>Maximum total uncompressed lookup bytes read per reader opening. Defaults to 16 MiB.</summary>
    public long CategoryTableByteLimit { get; set; } = 16 * 1024 * 1024;

    private readonly List<IDataEndpointReaderProvider> _providers = [];

    /// <summary>Configures format-name and path-extension mappings used before provider dispatch.</summary>
    public DataFormatResolutionOptions Formats { get; } = new();

    /// <summary>
    /// Adds a provider. Providers are evaluated in registration order and before built-in providers.
    /// Registering the same provider type more than once is rejected.
    /// </summary>
    public ResourceReaderFactoryOptions AddProvider(IDataEndpointReaderProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Any(candidate => candidate.GetType() == provider.GetType()))
            throw new InvalidOperationException(
                $"Reader provider '{provider.GetType().FullName}' is already registered.");
        _providers.Add(provider);
        return this;
    }

    internal IReadOnlyList<IDataEndpointReaderProvider> CombineWith(
        IEnumerable<IDataEndpointReaderProvider> builtInProviders)
    {
        var configuredTypes = _providers.Select(provider => provider.GetType()).ToHashSet();
        return [.. _providers, .. builtInProviders.Where(provider => !configuredTypes.Contains(provider.GetType()))];
    }
}
