using System.Globalization;
using System.Text.RegularExpressions;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using PocketCsvReader.Chrononuensis;
using PocketCsvReader.Configuration;

namespace Packata.ExtendedTypes;

internal static class ChrononuensisDelimitedBinding
{
    internal static DelimitedRepresentationBinding Binding { get; } =
        new(parsers => parsers.UseChrononuensis(), BindField);

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
