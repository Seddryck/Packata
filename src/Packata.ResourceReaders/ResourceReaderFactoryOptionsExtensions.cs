using Packata.ResourceReaders.Providers;

namespace Packata.ResourceReaders;

public static class ResourceReaderFactoryOptionsExtensions
{
    /// <summary>Reads declared URI and URI reference fields as <see cref="Uri"/> values.</summary>
    public static ResourceReaderFactoryOptions UseUriValues(this ResourceReaderFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddRepresentationProfile(InternetValueProfiles.UriValues);
    }

    /// <summary>Reads declared IPv4 and IPv6 fields as <see cref="System.Net.IPAddress"/> values.</summary>
    public static ResourceReaderFactoryOptions UseNetworkValues(this ResourceReaderFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddRepresentationProfile(InternetValueProfiles.NetworkValues);
    }

    /// <summary>Configures the built-in CSV, TSV, and PSV reader.</summary>
    public static ResourceReaderFactoryOptions AddDelimited(this ResourceReaderFactoryOptions options,
        Action<DelimitedReaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var delimited = new DelimitedReaderOptions();
        configure?.Invoke(delimited);
        return options.AddProvider(new DelimitedReaderProvider(delimited.DialectResolvers));
    }
}
