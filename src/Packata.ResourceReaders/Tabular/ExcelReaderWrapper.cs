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
        do
        {
            if ((!string.IsNullOrEmpty(Dialect!.SheetName) && reader.Name == Dialect.SheetName)
                || (Dialect.SheetNumber is not null && Dialect.SheetNumber == sheetNumber))
                    break;
        } while (reader.NextResult());

        if (Dialect.HeaderRows is not null && Dialect.HeaderRows.Any())
        {
            for (int i = 0; i < Dialect.HeaderRows.Max(); i++)
                reader.Read();
        }
        return reader;
    }

}
