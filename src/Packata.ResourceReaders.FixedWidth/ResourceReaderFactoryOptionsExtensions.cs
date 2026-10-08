using Packata.ResourceReaders.FixedWidth.Providers;

namespace Packata.ResourceReaders.FixedWidth;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddFixedWidth(this ResourceReaderFactoryOptions options) =>
        options.AddProvider(new FixedWidthReaderProvider());
}
