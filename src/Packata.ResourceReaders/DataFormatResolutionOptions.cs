namespace Packata.ResourceReaders;

/// <summary>Configures external format identifiers before reader-provider dispatch.</summary>
public sealed class DataFormatResolutionOptions
{
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _extensions = new(StringComparer.OrdinalIgnoreCase);

    public DataFormatResolutionOptions()
    {
        AddAlias("fixedwidth", DataFormatNames.FixedWidth);
        AddAlias("fwf", DataFormatNames.FixedWidth);
        AddExtension(".fwf", DataFormatNames.FixedWidth);
    }

    /// <summary>Maps a declared format name to the canonical name used by providers.</summary>
    public DataFormatResolutionOptions AddAlias(string alias, string canonicalName)
    {
        _aliases[Normalize(alias, nameof(alias))] = Normalize(canonicalName, nameof(canonicalName));
        return this;
    }

    /// <summary>Maps a path extension to the canonical name used by providers.</summary>
    public DataFormatResolutionOptions AddExtension(string extension, string canonicalName)
    {
        _extensions[Normalize(extension, nameof(extension))] = Normalize(canonicalName, nameof(canonicalName));
        return this;
    }

    internal IReadOnlyDictionary<string, string> Aliases => new Dictionary<string, string>(_aliases);
    internal IReadOnlyDictionary<string, string> Extensions => new Dictionary<string, string>(_extensions);

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim().TrimStart('.').ToLowerInvariant();
    }
}
