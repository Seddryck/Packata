using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

/// <summary>Configures providers evaluated before the built-in resource readers.</summary>
public sealed class ResourceReaderFactoryOptions
{
    /// <summary>Decode inline and local category-table values to labels for the PocketCsvReader delimited engine only.
    /// Disabled by default. Columns with labels return strings, including fallback code text.
    /// Native sequences match exact unquoted CSV text; quoted codes remain unchanged.</summary>
    /// <value>
    /// Decode inline and local category-table values to labels for the PocketCsvReader delimited engine only.
    /// Disabled by default. Columns with labels return strings, including fallback code text.
    /// Native sequences match exact unquoted CSV text; quoted codes remain unchanged.
    /// </value>
    public bool DecodeCategories { get; set; }

    /// <summary>Select category labels by language; implies DecodeCategories. Exclusive with DecodeCategoriesLabel.</summary>
    /// <value>
    /// Select category labels by language; implies DecodeCategories. Exclusive with DecodeCategoriesLabel.
    /// </value>
    public string? DecodeCategoriesLanguage { get; set; }

    /// <summary>Select an exact declared logical label field; implies DecodeCategories. Exclusive with DecodeCategoriesLanguage.</summary>
    /// <value>
    /// Select an exact declared logical label field; implies DecodeCategories. Exclusive with DecodeCategoriesLanguage.
    /// </value>
    public string? DecodeCategoriesLabel { get; set; }

    /// <summary>Receives one fallback diagnostic per affected category table per reader opening.</summary>
    /// <value>
    /// Receives one fallback diagnostic per affected category table per reader opening.
    /// </value>
    public Action<string>? CategoryDecodingDiagnostic { get; set; }

    /// <summary>Maximum total category-table rows loaded per reader opening. Defaults to 100,000.</summary>
    /// <value>
    /// Maximum total category-table rows loaded per reader opening. Defaults to 100,000.
    /// </value>
    public int CategoryTableRowLimit { get; set; } = 100_000;

    /// <summary>Maximum total uncompressed lookup bytes read per reader opening. Defaults to 16 MiB.</summary>
    /// <value>
    /// Maximum total uncompressed lookup bytes read per reader opening. Defaults to 16 MiB.
    /// </value>
    public long CategoryTableByteLimit { get; set; } = 16 * 1024 * 1024;

    private readonly List<IDataEndpointReaderProvider> _providers = [];
    private readonly Dictionary<string, ReaderRepresentationProfile> _profiles = new(StringComparer.Ordinal);

    private readonly List<ReaderValueConverter> _converters = [];

    internal ReaderRepresentationProfile[] GetRepresentationProfiles() => _profiles.Values.ToArray();

    internal ReaderValueConverter[] GetValueConverters() => _converters.ToArray();

    /// <summary>Selects a CLR representation for fields matched by the profile.</summary>
    /// <remarks>Return null for fields that use another profile or the reader's default representation.
    /// The selector must support concurrent calls.</remarks>
    public ResourceReaderFactoryOptions AddRepresentationProfile(string name, Func<DataField, Type?> selectType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selectType);
        if (!_profiles.TryAdd(name, new(name, selectType)) && _profiles[name].SelectType != selectType)
            throw new InvalidOperationException($"Representation profile '{name}' is already registered.");
        return this;
    }

    /// <summary>Registers a fallback from a native source type to the requested representation.</summary>
    /// <remarks>Omitted logical type, format, or encoding can match any declaration. The target type defaults to
    /// <typeparamref name="TResult"/> and, when supplied explicitly, must match its underlying CLR type.
    /// Converters shared by concurrent reader openings must be thread-safe and return values that remain
    /// usable after the reader advances or is disposed.</remarks>
    public ResourceReaderFactoryOptions AddValueConverter<TSource, TResult>(Func<TSource, TResult> convert,
        string? logicalType = null, string? format = null, SpatialEncoding? encoding = null, Type? targetType = null)
    {
        ArgumentNullException.ThrowIfNull(convert);
        _converters.Add(new(typeof(TSource), targetType ?? typeof(TResult), typeof(TResult),
            value => convert((TSource)value), logicalType, format, encoding));
        return this;
    }

    /// <summary>Configures format-name and path-extension mappings used before provider dispatch.</summary>
    /// <value>
    /// Configures format-name and path-extension mappings used before provider dispatch.
    /// </value>
    public DataFormatResolutionOptions Formats { get; } = new();

    /// <summary>
    /// Adds a provider. Providers are evaluated in registration order and before built-in providers.
    /// Registering the same provider type more than once is rejected.
    /// </summary>
    public ResourceReaderFactoryOptions AddProvider(IDataEndpointReaderProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Any(candidate => candidate.GetType() == provider.GetType()))
        {
            throw new InvalidOperationException(
                $"Reader provider '{provider.GetType().FullName}' is already registered.");
        }

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
