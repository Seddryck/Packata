using Packata.ResourceReaders.WebLogs.Providers;

namespace Packata.ResourceReaders.WebLogs;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddWebLogs(this ResourceReaderFactoryOptions options) =>
        options.AddProvider(new WebLogReaderProvider());
}
