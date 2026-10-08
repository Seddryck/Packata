namespace Packata.ResourceReaders;

/// <summary>Configures providers evaluated before the built-in resource readers.</summary>
public sealed class ResourceReaderFactoryOptions
{
    private readonly List<IDataEndpointReaderProvider> _providers = [];

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
