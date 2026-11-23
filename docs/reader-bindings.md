# Reader representation bindings

A reader factory resolves a CLR representation for each bound field from its canonical declaration and enabled profile. A reader contributes native bindings for the types and encodings it can produce. Packata tries a native binding first, then an explicitly registered converter from a supported source type. If both paths are unavailable, opening the reader reports the reader, field, and requested type.

## Configure a fallback converter

Use `AddValueConverter<TSource, TResult>()` with the reader factory options. The result type must match the requested representation. Optional logical type, format, and encoding restrict which declarations can use the converter.

```csharp
var factory = ResourceReaderFactory.Create(options =>
{
    options.UseNetTopologySuite();
    options.AddValueConverter<JsonElement, Geometry>(
        ParseGeometry, logicalType: "geojson", encoding: SpatialEncoding.GeoJson);
});
```

In this example, `ParseGeometry` is an application-supplied function. When the selected reader has a native `Geometry` binding, that binding handles the field. When it can supply `JsonElement`, Packata can invoke the converter and expose `Geometry` through `GetFieldType()`, `GetValue()`, and `GetFieldValue<Geometry>()`. Missing cells remain `DBNull`. Parse failures from a selected native binding remain conversion errors.

## Contribute a native binding from another package

1. Use the reader provider's stable `BindingKey`. A provider in the same assembly as its bindings can implement `IReaderBindingProvider` directly.
2. Implement `IReaderBindingProvider.TryBind` in a public class with a public parameterless constructor. Match the requested field, CLR type, and encoding. Return a `ReaderFieldBinding` containing the same CLR type and any reader-specific configuration.
3. Have the reader consume `ReaderOpenContext.FieldBindings` by canonical schema ordinal. When a fallback is selected, the binding describes the converter's source type. Keep columns in the canonical schema order. The reader can expose decoded text through `GetUnconvertedText()` for Packata to apply missing-value markers before parsing. A reader that supplies already typed values applies the markers before creating those values.
4. Package a manifest and an MSBuild target under `buildTransitive`. The target copies the manifest to `Packata.ReaderBindings` in the application output and publish directories. Use a unique file name ending in `.readerbindings.json`.

For example, a package contributing a native binding for a reader with key `example.ndjson` could ship this manifest:

```json
[
  {
    "id": "Example.Ndjson/NetTopologySuite",
    "readerKey": "example.ndjson",
    "profileName": "NetTopologySuite",
    "assemblyName": "Example.Ndjson",
    "providerType": "Example.Ndjson.GeometryBindingProvider"
  }
]
```

A contribution without `profileName` is available to the default profile and as a native source for converters. A manifest entry with a profile name becomes available when the factory enables that profile. See the bundled [manifest](../src/Packata.ExtendedTypes/Packata.ExtendedTypes.readerbindings.json) and [MSBuild target](../src/Packata.ExtendedTypes/Packata.ExtendedTypes.targets) for the package layout. The application references the integration package and configures the reader and profile as usual. Packata instantiates the binding provider.
