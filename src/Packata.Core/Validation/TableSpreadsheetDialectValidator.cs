using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class TableSpreadsheetDialectValidator : IValidator<TableSpreadsheetDialect>
{
    public bool IsValid(TableSpreadsheetDialect dialect)
        => IsNumberOrName(dialect, out _) && IsHeaderCoherent(dialect, out _);

    protected virtual bool IsNumberOrName(TableSpreadsheetDialect dialect, out Exception? exception)
    {
        exception = dialect.SheetName is not null ^ dialect.SheetNumber is not null
            ? null
            : new ArgumentOutOfRangeException(nameof(dialect), $"It cannot be both 'sheetName' and 'sheetNumber' strategies.");
        return exception is null;
    }

    protected virtual bool IsHeaderCoherent(TableSpreadsheetDialect dialect, out Exception? exception)
    {
        exception = (dialect.Header && (dialect.HeaderRows is null || dialect.HeaderRows.Count() == 0))
                    || (!dialect.Header && (dialect.HeaderRows is not null && dialect.HeaderRows.Count() > 0))
            ? new ArgumentOutOfRangeException(nameof(dialect), $"Properties 'header' and 'headerRows' are not coherent.")
            : null;
        return exception is null;
    }
}
