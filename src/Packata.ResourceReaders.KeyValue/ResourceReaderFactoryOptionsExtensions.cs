using Packata.ResourceReaders.KeyValue.Providers;

namespace Packata.ResourceReaders.KeyValue;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddKeyValueReaders(this ResourceReaderFactoryOptions options) =>
        options.AddProvider(new KeyValueReaderProvider());
}
