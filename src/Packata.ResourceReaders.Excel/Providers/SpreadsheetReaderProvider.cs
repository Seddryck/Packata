using System.Data;
using System.Text;
using ExcelDataReader;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Excel.Providers;

internal sealed class SpreadsheetReaderProvider : IDataEndpointReaderProvider
{
    public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) =>
        request.Endpoint.Location is PathLocation && format.Name is "xlsx" or "xls";

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Streams.Count != 1)
            throw new InvalidOperationException("Spreadsheet endpoints require exactly one path.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var reader = ExcelReaderFactory.CreateReader(context.Streams[0]);
        var (sheetNumber, sheetName) = ResolveSheet(context.Endpoint.Format);
        MoveToSheet(reader, sheetNumber, sheetName);
        IDataReader dataReader = new Tabular.ExcelDataReader(reader, ReadHeaders(reader, context.Endpoint.Format));
        return ValueTask.FromResult(dataReader);
    }

    private static (int Number, string? Name) ResolveSheet(DataFormat? format)
    {
        var sheetNumber = TryOption(format, "sheetNumber", out int number) ? number : 1;
        var sheetName = TryOption(format, "sheetName", out string? name) ? name : null;
        if (sheetName is not null && TryOption<int>(format, "sheetNumber", out _))
            throw new ArgumentException("Specify either sheetName or sheetNumber, not both.", nameof(format));
        return (sheetNumber, sheetName);
    }

    private static void MoveToSheet(IExcelDataReader reader, int sheetNumber, string? sheetName)
    {
        for (var current = 1; current < sheetNumber || (sheetName is not null && reader.Name != sheetName); current++)
            if (!reader.NextResult()) throw new InvalidOperationException("The configured spreadsheet sheet was not found.");
    }

    private static string[] ReadHeaders(IExcelDataReader reader, DataFormat? format)
    {
        var headers = new List<string>();
        var hasHeader = !TryOption(format, "header", out bool header) || header;
        if (hasHeader && reader.Read())
            for (var index = 0; index < reader.FieldCount; index++)
                headers.Add(reader.GetValue(index)?.ToString() ?? string.Empty);
        return [.. headers];
    }

    private static bool TryOption<T>(DataFormat? format, string name, out T value)
    {
        if (format?.Options.TryGetValue(name, out var raw) == true && raw is T typed)
        { value = typed; return true; }
        value = default!;
        return false;
    }
}
