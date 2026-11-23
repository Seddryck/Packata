using System.Text.Json;
using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

internal static class DelimitedSpatialFields
{
    internal static bool TryAdd(ISchemaDescriptorBuilder schema, DataField field)
    {
        var logicalType = field.LogicalType?.Trim().ToLowerInvariant();
        if (logicalType is not ("geopoint" or "geojson")) return false;

        var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        var diagnostic = resolution.Diagnostics.FirstOrDefault(item =>
            item.Severity == TypeMappingSeverity.Error && item.Target == TypeMappingTarget.Source);
        if (diagnostic is not null)
            throw new NotSupportedException($"Cannot bind CSV field '{field.Name}'. {diagnostic.Message}");

        if (resolution.RuntimeType == typeof(GeoPoint))
        {
            var format = GetGeoPointFormat(field);
            schema.WithCustomField(typeof(GeoPoint), field.Name,
                builder => builder.WithDataSourceTypeName(logicalType).WithParser<GeoPoint>(text => GeoPointParser.Parse(text, format)));
            return true;
        }

        if (resolution.RuntimeType == typeof(JsonElement))
        {
            schema.WithCustomField(typeof(JsonElement), field.Name,
                builder => builder.WithDataSourceTypeName(logicalType)
                    .WithParser<JsonElement>(text => JsonSerializer.Deserialize<JsonElement>(text)));
            return true;
        }

        throw new NotSupportedException($"Cannot bind CSV field '{field.Name}' as '{logicalType}'.");
    }

    internal static GeoPointFormat GetGeoPointFormat(DataField field) => field.Format?.Trim().ToLowerInvariant() switch
    {
        "array" => GeoPointFormat.Array,
        "object" => GeoPointFormat.Object,
        _ => GeoPointFormat.Default
    };
}
