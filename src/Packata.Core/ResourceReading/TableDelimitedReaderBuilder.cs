using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace Packata.Core.ResourceReading;
public class TableDelimitedReaderBuilder : IResourceReaderBuilder
{
    private CsvReaderBuilder? _csvReaderBuilder;
    private RuntimeTypeMapper RuntimeTypes = new();
    private DefaultFormatMapper _defaultFormatMapper = new();
    private DateTimeFormatConverter _dateTimeFormatConverter = new();

    public void Configure(Resource resource)
        => _csvReaderBuilder = ConfigureBuilder(resource);

    public void Register(string type, Type runtimeType)
        => Register(type, null, runtimeType);

    public void Register(string type, string? format, Type runtimeType)
        => RuntimeTypes.Register(type, string.IsNullOrEmpty(format) ? null : format, runtimeType);

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
                FieldDescriptorBuilder enrich(FieldDescriptorBuilder builder, Field field)
                {
                    builder = field.Type is not null
                                ? builder.WithDataSourceTypeName(field.Type)
                                : builder;
                    withSequence(builder, [.. (field.MissingValues ?? resource.Schema!.MissingValues ?? [])]);
                    return builder;
                }

                if (field is NumberField numberField)
                {
                    schemaBuilder.WithNumberField(
                        RuntimeTypes.Map(field.Type, field.Format),
                        field.Name!,
                        builder =>
                        {
                            builder = builder.WithFormat((fmt) =>
                            {
                                fmt = numberField.GroupChar is not null
                                        ? fmt.WithGroupChar(numberField.GroupChar.Value)
                                        : fmt.WithoutGroupChar();
                                fmt = numberField.DecimalChar is not null
                                        ? fmt.WithDecimalChar(numberField.DecimalChar.Value)
                                        : fmt;
                                return fmt;
                            });
                            return (NumberFieldDescriptorBuilder)enrich(builder, field);
                        });
                }
                else if (field is IntegerField integerField)
                {
                    schemaBuilder.WithIntegerField(
                        RuntimeTypes.Map(field.Type, field.Format),
                        field.Name!,
                        builder =>
                        {
                            builder = builder.WithFormat((fmt) =>
                            {
                                fmt = integerField.GroupChar is not null
                                        ? fmt.WithGroupChar(integerField.GroupChar.Value)
                                        : fmt.WithoutGroupChar();
                                return fmt;
                            });
                            return (IntegerFieldDescriptorBuilder)enrich(builder, field);
                        });
                }
                else if (field is TemporalField temporalField)
                {
                    schemaBuilder.WithTemporalField(
                        RuntimeTypes.Map(field.Type, field.Format),
                        field.Name!,
                        builder =>
                        {
                            builder = builder.WithFormat(
                                _dateTimeFormatConverter.Convert(
                                    (temporalField.Format ?? "default").Equals("default", StringComparison.InvariantCultureIgnoreCase)
                                    && field.Type is not null
                                    && _defaultFormatMapper.TryGetMapping(field.Type, out var defaultFormat)
                                        ? defaultFormat
                                        : temporalField.Format!));
                            return (TemporalFieldDescriptorBuilder)enrich(builder, field);
                        });
                }
                else if (field is CustomField customField)
                {
                    schemaBuilder.WithCustomField(
                        RuntimeTypes.Map(field.Type, field.Format),
                        field.Name!,
                        builder =>
                        {
                            builder = customField.Format is not null ? builder.WithFormat(customField.Format) : builder;
                            return (CustomFieldDescriptorBuilder)enrich(builder, field);
                        });
                }
                else
                {
                    schemaBuilder.WithField(
                        RuntimeTypes.Map(field.Type, field.Format),
                        field.Name!,
                        builder => enrich(builder, field));
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
