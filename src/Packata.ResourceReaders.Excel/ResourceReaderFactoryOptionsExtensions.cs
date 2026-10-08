using Packata.ResourceReaders.Excel.Providers;

namespace Packata.ResourceReaders.Excel;

public static class ResourceReaderFactoryOptionsExtensions
{
    public static ResourceReaderFactoryOptions AddExcel(this ResourceReaderFactoryOptions options) =>
        options.AddProvider(new SpreadsheetReaderProvider());
}
