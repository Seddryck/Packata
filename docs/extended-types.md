# Extended CLR types

Install the optional `Packata.ExtendedTypes` NuGet package and enable the profiles your reader factory needs.

```csharp
using Packata.ExtendedTypes;
using Packata.ResourceReaders;

var factory = ResourceReaderFactory.Create(options =>
{
    options.UseNetTopologySuite();
    options.UseChrononuensis();
});
```

The package depends on `Packata.ResourceReaders`, `PocketCsvReader.NetTopologySuite`, and `PocketCsvReader.Chrononuensis`. The PocketCsvReader integration packages bring in NetTopologySuite and Chrononuensis. The bundled bindings support CSV, TSV, and PSV. A reader without a binding for a selected representation reports the field and requested CLR type when opened.

| Profile | Canonical declaration | CSV CLR type and handling |
| --- | --- | --- |
| Chrononuensis | `year` | `Chrononuensis.Year` |
| Chrononuensis | `yearmonth` | `Chrononuensis.YearMonth` |
| NetTopologySuite | `geopoint` with `default`, `array`, or `object` | `NetTopologySuite.Geometries.Point` with X = longitude and Y = latitude |
| NetTopologySuite | `geojson` with omitted or `default` format | `NetTopologySuite.Geometries.Geometry`. The native parser accepts geometry payloads and reports features and feature collections as conversion errors. |
| NetTopologySuite | `geojson` with `topojson` format | `System.Text.Json.JsonElement` preserves the topology. |
| NetTopologySuite | `geometry` or `geography` with WKT or GeoJSON encoding | The declared geometry subtype, such as `Point` or `LineString`, or `Geometry` when the subtype is omitted. |

Chrononuensis honors explicit field patterns and uses the field language as the culture when supplied. The NetTopologySuite profile reports WKB, EWKT, EWKB, and unspecified spatial encodings as unsupported. WKT uses the native parser default SRID of 0. GeoJSON geometry uses SRID 4326. With both profiles disabled, the built-in reader uses its default CLR mappings.
