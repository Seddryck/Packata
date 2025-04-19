using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using DubUrl.Querying.Dialects;
using Packata.Core;
using Packata.Core.ResourceReading;
using Packata.Core.Validation;

namespace Packata.ResourceReaders.Tabular;
public class SpreadsheetReaderBuilder : IResourceReaderBuilder
{
    private TableSpreadsheetDialect? _dialect;

    public void Configure(Resource resource)
    {
        _dialect = resource.Dialect as TableSpreadsheetDialect ?? new TableSpreadsheetDialect();
        var validator = new TableSpreadsheetDialectValidator();
        validator.Validate(_dialect);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IResourceReader Build()
    {
        if (_dialect == null)
            throw new InvalidOperationException("Configure method must be called before Build.");
        return new SpreadsheetReader(new ExcelReaderWrapper(_dialect));
    }
}
