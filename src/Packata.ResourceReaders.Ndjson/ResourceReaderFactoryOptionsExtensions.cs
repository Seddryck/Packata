using Packata.ResourceReaders.Ndjson.Providers;

namespace Packata.ResourceReaders.Ndjson;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddNdjson(this ResourceReaderFactoryOptions options) =>
        options.AddProvider(new NdjsonReaderProvider());
}
