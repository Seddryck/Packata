using Packata.ResourceReaders.Parquet.Providers;

namespace Packata.ResourceReaders.Parquet;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddParquet(this ResourceReaderFactoryOptions options) =>
        options.AddProvider(new ParquetReaderProvider());
}
