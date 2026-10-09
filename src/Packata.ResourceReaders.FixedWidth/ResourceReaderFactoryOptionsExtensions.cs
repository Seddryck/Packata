using Packata.ResourceReaders.FixedWidth.Providers;

namespace Packata.ResourceReaders.FixedWidth;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddFixedWidth(this ResourceReaderFactoryOptions options,
        Action<FixedWidthReaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fixedWidth = new FixedWidthReaderOptions();
        configure?.Invoke(fixedWidth);
        return options.AddProvider(new FixedWidthReaderProvider(fixedWidth.LayoutResolvers));
    }
}
