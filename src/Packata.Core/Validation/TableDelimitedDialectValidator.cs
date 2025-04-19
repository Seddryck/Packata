using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class TableDelimitedDialectValidator : IValidator<TableDelimitedDialect>
{
    public bool IsValid(TableDelimitedDialect tableDialect)
        => IsEscapeOrQuote(tableDialect, out var _) && IsHeaderCoherent(tableDialect, out var _);

    protected virtual bool IsEscapeOrQuote(TableDelimitedDialect dialect, out Exception? exception)
    {
        exception = dialect.EscapeChar is not null
                        && (dialect.QuoteChar is not null
                        || dialect.DoubleQuote)
            ? new ArgumentOutOfRangeException(nameof(dialect), $"It cannot be both 'escape' and 'quote' strategies.")
            : null;
        return exception is null;
    }

    protected virtual bool IsHeaderCoherent(TableDelimitedDialect dialect, out Exception? exception)
    {
        exception = (dialect.Header && (dialect.HeaderRows is null || !dialect.HeaderRows.Any()))
                    || (!dialect.Header && (dialect.HeaderRows is not null && dialect.HeaderRows.Any()))
            ? new ArgumentOutOfRangeException(nameof(dialect), $"Properties 'header' and 'headerRows' are not coherent.")
            : null;
        return exception is null;
    }
}
