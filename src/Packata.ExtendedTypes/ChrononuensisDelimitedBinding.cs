using System.Globalization;
using System.Text.RegularExpressions;
using Chrononuensis;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using PocketCsvReader.Chrononuensis;
using PocketCsvReader.Configuration;

namespace Packata.ExtendedTypes;

/// <summary>Binds Chrononuensis year and year-month fields in PocketCsvReader.</summary>
public sealed class ChrononuensisDelimitedBinding : IReaderBindingProvider
{
    private static readonly DelimitedFieldBinding Binding =
        new(BindField, parsers => parsers.UseChrononuensis());

    /// <inheritdoc />
    public ReaderFieldBinding? TryBind(ReaderRepresentationRequest request)
    {
        var logicalType = request.Field.LogicalType?.Trim().ToLowerInvariant();
        if ((logicalType == "year" && request.RuntimeType == typeof(Year))
            || (logicalType == "yearmonth" && request.RuntimeType == typeof(YearMonth)))
            return new(request.RuntimeType, Binding);

        return null;
    }

    private static void BindField(ISchemaDescriptorBuilder schema, DataField field, Type type)
    {
        var pattern = PocketCsvReaderProviderDefaults.ResolvePattern(field);
        if (pattern?.Contains('%') == true)
            pattern = TranslatePattern(pattern);
        var culture = string.IsNullOrWhiteSpace(field.Language) ? null : CultureInfo.GetCultureInfo(field.Language);
        schema.WithCustomField(type, field.Name, builder =>
        {
            builder.WithDataSourceTypeName(field.LogicalType!);
            if (pattern is not null)
                builder.WithFormat(pattern, culture);
            return builder;
        });
    }

    private static string TranslatePattern(string pattern)
    {
        // Chrononuensis uses quoted literals, while the strptime converter emits backslash escapes.
        return Regex.Replace(DateTimeFormatConverter.Convert(pattern), @"\\([\s\S])",
            match => match.Groups[1].Value == "'" ? "\"'\"" : $"'{match.Groups[1].Value}'");
    }
}
