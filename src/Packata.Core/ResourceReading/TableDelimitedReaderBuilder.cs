using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace Packata.Core.ResourceReading;
internal class TableDelimitedReaderBuilder : IResourceReaderBuilder
{
    private CsvReaderBuilder? _csvReaderBuilder;
    private Func<string?, string?, Type> MapRuntimeType = RuntimeTypeMapper.Map;
    private DefaultFormatMapper _defaultFormatMapper = new();

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
            {
                if (field is NumericField numericField)
                {
                    // Handle NumericField separately
                    schemaBuilder.WithNumericField(
                        MapRuntimeType(field.Type, field.Format),
                        field.Name!,
                        builder =>
                        {
                            builder = field.Format is not null
                                ? (NumericFieldDescriptorBuilder)builder.WithFormat(field.Format)
                                : builder;

                            builder = numericField is NumberField numberField && numberField.DecimalChar is not null
                                ? builder.WithDecimalChar(numberField.DecimalChar.Value)
                                : builder;

                            builder = numericField.GroupChar is not null
                                ? builder.WithGroupChar(numericField.GroupChar.Value)
                                : builder.WithoutGroupChar();

                            withSequence(builder, [.. (numericField.MissingValues ?? resource.Schema.MissingValues ?? [])]);

                            return builder;
                        });
                }
                else
                {
                    // General handling for non-numeric fields
                    schemaBuilder.WithField(
                        MapRuntimeType(field.Type, field.Format),
                        field.Name!,
                        builder =>
                        {
                            withSequence(builder, [ .. field.MissingValues ?? resource.Schema.MissingValues ?? []]);

                            return field.Format is not null
                                ? builder.WithFormat(
                                    field.Format.Equals("default", StringComparison.InvariantCultureIgnoreCase)
                                        && field.Type is not null
                                        && _defaultFormatMapper.TryGetMapping(field.Type, out var defaultFormat)
                                    ? defaultFormat
                                    : field.Format)
                                : builder;
                        });
                }

                FieldDescriptorBuilder withSequence(FieldDescriptorBuilder builder, List<MissingValue> missingValues) =>
                    missingValues.Count > 0
                        ? missingValues.Aggregate(builder, (b, missingValue) => b.WithSequence(missingValue.Value, null))
                        : builder;
            }
        }

        var resourceBuilder = new ResourceDescriptorBuilder();
        if (resource.Encoding is not null)
        {
            resourceBuilder.WithEncoding(resource.Encoding);
        }

        var csvReaderBuilder = new CsvReaderBuilder().WithDialect(dialectBuilder);
        csvReaderBuilder = schemaBuilder is not null ? csvReaderBuilder.WithSchema(schemaBuilder) : csvReaderBuilder;
        csvReaderBuilder = resourceBuilder is not null ? csvReaderBuilder.WithResource(resourceBuilder) : csvReaderBuilder;
        return csvReaderBuilder;
    }
}
