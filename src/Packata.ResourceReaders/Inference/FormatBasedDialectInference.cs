using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class FormatBasedDialectInference : IDialectInference
{
    public virtual bool TryInfer(Resource resource, [NotNullWhen(true)] out TableDialect? dialect)
        => TryInferFromFormat(resource.Format, out dialect);

    protected bool TryInferFromFormat(string? format, [NotNullWhen(true)] out TableDialect? dialect)
    {
        dialect = format switch
        {
            "csv" => new TableDelimitedDialect { Delimiter = ',', QuoteChar = '"', DoubleQuote = true, LineTerminator = "\r\n" },
            "tsv" => new TableDelimitedDialect { Delimiter = '\t', QuoteChar = '"', DoubleQuote = true, LineTerminator = "\r\n" },
            "psv" => new TableDelimitedDialect { Delimiter = '|', EscapeChar = '\\', DoubleQuote = false, LineTerminator = "\r\n" },
            _ => null
        };
        return dialect is not null;
    }
}
