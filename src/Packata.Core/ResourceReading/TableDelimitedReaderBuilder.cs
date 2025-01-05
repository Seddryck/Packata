using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace Packata.Core.ResourceReading;
internal class TableDelimitedReaderBuilder : IResourceReaderBuilder
{
    private CsvReaderBuilder? _csvReaderBuilder;
    private Func<string?, string?, Type> MapRuntimeType = RuntimeTypeMapper.Map;

    public void Configure(Resource resource)
        => _csvReaderBuilder = ConfigureBuilder(resource);

    public void Configure(Func<string?, string?, Type> mapRuntimeType)
        => MapRuntimeType = mapRuntimeType;

    public IResourceReader Build()
    {
        if (_csvReaderBuilder is null)
            throw new InvalidOperationException("Builder not configured");
        return new TableDelimitedReader(_csvReaderBuilder.Build());
    }

    protected virtual CsvReaderBuilder ConfigureBuilder(Resource resource)
    {
        var dialectBuilder = new DialectDescriptorBuilder();

        if (resource.Dialect is not null)
        {
            var dialect = resource.Dialect;
            dialectBuilder.WithDelimiter(dialect.Delimiter)
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


        ISchemaDescriptorBuilder? schemaBuilder = null;
        if (resource.Schema is not null && resource.Schema.Fields.Count > 0)
        {
            schemaBuilder = resource.Schema.FieldsMatch == FieldsMatching.Exact
                            ? new SchemaDescriptorBuilder().Indexed()
                            : new SchemaDescriptorBuilder().Named();
            foreach (var field in resource.Schema.Fields)
                schemaBuilder.WithField(
                    MapRuntimeType(field.Type, field.Format)
                    , field.Name!
                    , f => field.Format is null ? f : f.WithFormat(field.Format)
                );
        }

        var csvReaderBuilder = new CsvReaderBuilder().WithDialect(dialectBuilder);
        csvReaderBuilder = schemaBuilder is not null ? csvReaderBuilder.WithSchema(schemaBuilder) : csvReaderBuilder;
        return csvReaderBuilder;
    }
}
