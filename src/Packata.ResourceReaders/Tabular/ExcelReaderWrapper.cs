using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;
using Packata.Core;

namespace Packata.ResourceReaders.Tabular;
internal class ExcelReaderWrapper
{
    protected TableSpreadsheetDialect Dialect { get; }

    public ExcelReaderWrapper(TableSpreadsheetDialect dialect)
    {
        Dialect = dialect;
    }

    public IDataReader ToDataReader(Stream stream)
    {
        var config = new ExcelReaderConfiguration();
        var reader = ExcelReaderFactory.CreateReader(stream, config);

        var sheetNumber = 1;
        bool hasNext = true;
        while (hasNext)
        {
            if ((!string.IsNullOrEmpty(Dialect!.SheetName) && reader.Name == Dialect.SheetName)
                || (Dialect.SheetNumber is not null && Dialect.SheetNumber == sheetNumber))
                    break;
            sheetNumber++;
            hasNext = reader.NextResult();
        } 

        if (!string.IsNullOrEmpty(Dialect!.SheetName) && reader.Name != Dialect.SheetName)
            throw new InvalidOperationException($"Sheet '{Dialect.SheetName}' not found in the Excel file.");

        if (Dialect.SheetNumber is not null && !hasNext)
            throw new InvalidOperationException($"Sheet '{Dialect.SheetNumber}' not found in the Excel file.");

        if (Dialect.HeaderRows is not null && Dialect.HeaderRows.Any())
        {
            for (int i = 0; i < Dialect.HeaderRows.Max(); i++)
                reader.Read();
        }
        return reader;
    }
}
