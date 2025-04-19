using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class TableSpreadsheetDialectValidator : IValidator<TableSpreadsheetDialect>
{
    public bool IsValid(TableSpreadsheetDialect dialect)
        => IsNumberOrName(dialect, out _) && IsHeaderCoherent(dialect, out _);

    public void Validate(TableSpreadsheetDialect dialect)
    {
        var list = new List<Exception>();
        if (!IsNumberOrName(dialect, out var exception))
            list.Add(exception);
        if (!IsHeaderCoherent(dialect, out exception))
            list.Add(exception);

        if (list.Count == 0)
            return;

        throw new AggregateException("Validation of spreadsheet dialect failed.", [.. list]);
    }

    protected virtual bool IsNumberOrName(TableSpreadsheetDialect dialect, [NotNullWhen(false)] out Exception? exception)
    {
        exception = dialect.SheetName is not null ^ dialect.SheetNumber is not null
            ? null
            : new ArgumentOutOfRangeException(nameof(dialect), $"It cannot be both 'sheetName' and 'sheetNumber' strategies.");
        return exception is null;
    }

    protected virtual bool IsHeaderCoherent(TableSpreadsheetDialect dialect, [NotNullWhen(false)] out Exception? exception)
    {
        exception = (dialect.Header && (dialect.HeaderRows is null || dialect.HeaderRows.Count() == 0))
                    || (!dialect.Header && (dialect.HeaderRows is not null && dialect.HeaderRows.Count() > 0))
            ? new ArgumentOutOfRangeException(nameof(dialect), $"Properties 'header' and 'headerRows' are not coherent.")
            : null;
        return exception is null;
    }
}
