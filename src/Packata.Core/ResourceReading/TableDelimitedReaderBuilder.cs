using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace Packata.Core.ResourceReading;
internal class TableDelimitedReaderBuilder : IResourceReaderBuilder
{
    private CsvReaderBuilder? _csvReaderBuilder;

    public void Configure(Resource resource)
        => _csvReaderBuilder = ConfigureBuilder(resource);

    public IResourceReader Build()
    {
        if (_csvReaderBuilder is null)
            throw new InvalidOperationException("Builder not configured");
        return new TableDelimitedReader(_csvReaderBuilder.Build());
    }

    protected virtual CsvReaderBuilder ConfigureBuilder(Resource resource)
    {
        var builder = new DialectDescriptorBuilder();

        if (resource.Dialect is not null)
        {
            var dialect = resource.Dialect;
            builder.WithDelimiter(dialect.Delimiter)
                .WithLineTerminator(dialect.LineTerminator)
                .WithQuoteChar(dialect.QuoteChar)
                .WithDoubleQuote(dialect.DoubleQuote)
                .WithEscapeChar(dialect.EscapeChar)
                .WithHeader(dialect.Header)
                .WithHeaderJoin(dialect.HeaderJoin ?? "")
                .WithHeaderRows(dialect.HeaderRows?.ToArray() ?? [])
                .WithCommentChar(dialect.CommentChar)
                .WithCommentRows(dialect.CommentRows?.ToArray() ?? [])
                .WithSkipInitialSpace(dialect.SkipInitialSpace);
        }

        var csvReaderBuilder = new CsvReaderBuilder()
            .WithDialect(b => builder);
        return csvReaderBuilder;
    }
}
