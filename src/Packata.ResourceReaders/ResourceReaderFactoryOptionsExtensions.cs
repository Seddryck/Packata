using Packata.ResourceReaders.Providers;

namespace Packata.ResourceReaders;

public static class ResourceReaderFactoryOptionsExtensions
{
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
