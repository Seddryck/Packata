using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class TableDialectValidator : IValidator<TableDelimitedDialect>
{
    public bool IsValid(TableDelimitedDialect tableDialect)
        => IsEscapeOrQuote(tableDialect, out var _) && IsHeaderCoherent(tableDialect, out var _);

    protected virtual bool IsEscapeOrQuote(TableDelimitedDialect TableDialect, out Exception? exception)
    {
        exception = TableDialect.EscapeChar is not null
                        && (TableDialect.QuoteChar is not null
                        || TableDialect.DoubleQuote)
            ? new ArgumentOutOfRangeException($"It cannot be both 'escape' and 'quote' strategies.")
            : null;
        return exception is null;
    }

    protected virtual bool IsHeaderCoherent(TableDelimitedDialect TableDialect, out Exception? exception)
    {
        exception = (TableDialect.Header && (TableDialect.HeaderRows is null || TableDialect.HeaderRows.Count() == 0))
                    || (!TableDialect.Header && (TableDialect.HeaderRows is not null && TableDialect.HeaderRows.Count() > 0))
            ? new ArgumentOutOfRangeException($"Properties 'header' and 'headerRows' are not coherent.")
            : null;
        return exception is null;
    }
}
