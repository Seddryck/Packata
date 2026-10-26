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

    public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context, CancellationToken cancellationToken = default)
    {
        if (context.Streams.Count != 1)
            throw new InvalidOperationException("Spreadsheet endpoints require exactly one path.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var reader = ExcelReaderFactory.CreateReader(context.Streams[0], new ExcelReaderConfiguration { LeaveOpen = true });
        try
        {
            var format = context.Endpoint.Format;
            SpreadsheetLayout.SelectSheet(reader, SpreadsheetLayout.Option(format, "sheetName")?.ToString(),
                SpreadsheetLayout.Number(format, "sheetNumber"));
            var skip = SpreadsheetLayout.LeadingRows(format);
            for (var index = 0; index < skip; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reader.Read()) break;
            }
            var values = new object?[reader.FieldCount];
            if ((SpreadsheetLayout.Header(format) ?? true) && reader.Read()) reader.GetValues(values!);
            var names = context.Schema is { Fields.Count: > 0 } schema
                ? schema.Fields.Select(field => field.Name).ToArray() : SpreadsheetLayout.Names(values);
            IDataReader dataReader = new Tabular.ExcelDataReader(reader, names);
            return ValueTask.FromResult(dataReader);
        }
        catch { reader.Dispose(); throw; }
    }
}
