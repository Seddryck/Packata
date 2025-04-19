using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using DubUrl.Registering;
using ExcelDataReader;
using Packata.Core;
using Packata.Core.ResourceReading;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders.Tabular;
public class SpreadsheetReaderBuilder : IResourceReaderBuilder
{
    private TableSpreadsheetDialect? _dialect;

    public void Configure(Resource resource)
    {
        _dialect = resource.Dialect as TableSpreadsheetDialect ?? new TableSpreadsheetDialect();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IResourceReader Build()
        => new SpreadsheetReader(new ExcelReaderWrapper(_dialect!));
}
