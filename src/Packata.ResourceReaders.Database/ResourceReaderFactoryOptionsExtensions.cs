using Packata.ResourceReaders.Database.Providers;

namespace Packata.ResourceReaders.Database;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddDatabase(this ResourceReaderFactoryOptions options,
        string? rootPath = null) =>
        options.AddProvider(new DatabaseReaderProvider(new DubUrlDatabaseSessionFactory(rootPath ?? string.Empty)));
}
