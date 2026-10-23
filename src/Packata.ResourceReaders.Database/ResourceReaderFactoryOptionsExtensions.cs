using Packata.ResourceReaders.Database.Providers;
using Packata.Database;

namespace Packata.ResourceReaders.Database;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddDatabase(this ResourceReaderFactoryOptions options,
        string? rootPath = null) =>
        options.AddDatabase(rootPath is null ? DubUrlConnectionService.Default : new DubUrlConnectionService(rootPath));

    /// <summary>The host owns the shared service; returned readers own their operation's connection.</summary>
    public static ResourceReaderFactoryOptions AddDatabase(this ResourceReaderFactoryOptions options,
        IDatabaseConnectionService connections) =>
        options.AddProvider(new DatabaseReaderProvider(connections));
}
