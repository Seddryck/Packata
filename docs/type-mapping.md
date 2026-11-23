---
title: Field type mapping
parent: Reference
nav_order: 3
permalink: /reference/dotnet/type-mapping/
---

`Packata.Core` interprets canonical `DataField` declarations through a shared resolver.
It describes representations and their limits; it does not validate dataset values or
enforce all schema constraints. No code-generation or optional target-library dependency
is required.

```csharp
using Packata.Core;
using Packata.Core.Contracts;

var field = new DataField("count", "integer", Format: "u64", Required: true, Nullable: true);
var mapping = new RuntimeTypeMapper().Resolve(field, TypeMappingOptions.Strict);
// RuntimeType: typeof(ulong); DatabaseType: DbType.UInt64
// Parameters: IntegerWidth = 64, Signed = false
// Required and Nullable are both true; RuntimeType is never wrapped in Nullable<T>.
```

`FieldTypeResolver.Resolve(field, options)` returns both projections from one interpretation.
`RuntimeTypeMapper.Resolve` and `DbTypeMapper.Resolve` additionally apply that adapter's
custom registrations and format hooks to its own projection. The other projection remains
the shared default. Inspect each projection and the diagnostics independently; a valid
database decimal can have a null `RuntimeType`.

## Interpretation and precedence

1. The canonical logical type determines the type family. The rich resolver trims and
   lowercases logical type names; `decimal` is an alias for `number`.
2. A recognized semantic format selects the representation. Integers support `i8`, `u8`,
   `i16`, `u16`, `i32`, `u32`, `i64`, `u64`; numbers support `fp32`, `fp64`, and `decimal`.
   Format width/signedness takes precedence over conflicting `integerWidth`/`signed`
   constraints, with a `ConflictingSemantics` warning. Without a format, both integer
   parameters must be supplied together to select a domain.
3. Canonical constraints supply declared `precision`, `scale`, `timezone`,
   `defaultTimezone`, `integerWidth`, and `signed` parameters. The first constraint with
   each name is used. Absent parameters remain absent; default CLR representations do
   not imply that the source declared their limits. ODCS mapping exposes declared type
   options in these constraints, and database inference exposes numeric precision/scale
   consistently for tables and views while retaining native metadata.
   Inferred integer formats now preserve native signedness and width (`Byte` becomes
   `u8`, `UInt64` becomes `u64`) instead of describing them as signed integers.
   Inference also sets canonical value nullability from database column metadata,
   independently of retaining its existing requiredness mapping.
4. A valid strptime temporal pattern is parsing information, independently of numeric
   representation formats. Unescaped `%z`/`%:z` declares offset semantics and takes
   precedence over a conflicting `timezone: false`, with a warning. Escaped directives
   have no such effect. Unsupported directives, including `%Z`, are diagnosed.
5. Physical database type spellings are retained on `Field` and in diagnostics but never
   guessed by Core. Native types, dialect context, native dimensions and SQL rendering
   remain in the DubUrl/provisioning boundary. For example, a bare physical `timestamp`
   never overrides a canonical logical type.

Unformatted integers retain the existing `Int32` default, and unformatted numbers use
`Decimal`. Strings retain string representation for recognized semantic annotations
such as `uuid`, `email`, or `binary`; mapping these annotations does not validate or
convert their values. Unknown formats produce `UnsupportedFormat` rather than silently
selecting an unformatted representation.

## Spatial declarations

Canonical `geopoint` resolves to `Packata.Core.GeoPoint`, an immutable value type with
`double Longitude` and `double Latitude` properties and value equality. DPv2 Table
Schema defines three input formats, all with the same CLR representation.

| Logical type | Format | Input representation | CLR type |
| --- | --- | --- | --- |
| `geopoint` | omitted or `default` | Longitude and latitude text, such as `"4.35, 50.85"` | `GeoPoint` |
| `geopoint` | `array` | Exactly two JSON numbers, such as `[4.35, 50.85]` | `GeoPoint` |
| `geopoint` | `object` | Exactly the numeric `lon` and `lat` members | `GeoPoint` |
| `geojson` | omitted or `default` | GeoJSON geometry, feature, or feature collection | `System.Text.Json.JsonElement` |
| `geojson` | `topojson` | TopoJSON object | `System.Text.Json.JsonElement` |

The positional point formats use longitude first and latitude second. The Data Package
mapper preserves the declared logical type and explicit format, including an omitted
format. Strict resolution interprets omission as `default`. Other spatial formats,
including `any`, produce a source `UnsupportedFormat` error. These encodings come from
[DPv2 Table Schema](https://datapackage.org/standard/table-schema/#geopoint).

```csharp
var location = new GeoPoint(Longitude: 4.35, Latitude: 50.85);
var field = new DataField("location", "geopoint", Format: "array");
var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
// RuntimeType is typeof(GeoPoint).
```

Spatial types have a null strict `DatabaseType` and an
`UnsupportedRepresentation` diagnostic targeted at `Database`. Their CLR projections
remain available. Dialect-specific storage and export adapters determine their own
supported representations. Explicit database fallbacks retain that diagnostic.

`DataField.Spatial` describes schema meaning separately from the CLR value: `Model`
(`Unspecified`, planar `Geometry`, or geodetic `Geography`), `SubType`, `Crs`,
`Dimensions`, `Encoding`, and `Content`. It also retains `Algorithm`, `BoundingBox`,
`Orientation`, and decimal-year `Epoch`. Bounding boxes are copied into an immutable
snapshot. Switching a CLR profile does not change this descriptor.

DPv2 geopoints declare a two-dimensional Point with unspecified spatial model and
CRS. Their input encoding remains in `Format`; the mapper does not invent a WKT or
GeoJSON encoding. Generic DPv2 GeoJSON has `Content: GeoJsonValue`, which includes
Feature and FeatureCollection; TopoJSON has `Content: Topology`. Neither implies a
particular geometry subtype or spatial model.

### Proposed ODCS RFC-0048 profile

[Bitol RFC-0048](https://github.com/bitol-io/tsc/blob/main/rfcs/0048-geometry-geography.md)
is a proposal, outside official ODCS v3.2.0. Mapping `logicalType: geometry` or
`geography` requires `new OdcsMappingOptions { EnableSpatialRfc0048 = true }`.
The existing ODCS mapper maps `logicalTypeOptions` into the spatial descriptor and
also retains the original options in `Extensions["odcs"]["logicalTypeOptions"]`.
`geometry` requires an explicit CRS. Omitted geography CRS and dimensions stay absent
in the canonical declaration, even though the RFC defines interpretation defaults
of EPSG:4326 and two dimensions. Three dimensions do not establish whether the third
ordinate is Z or M; four dimensions represent XYZM.

| Canonical geometry/geography encoding | Default CLR type |
| --- | --- |
| `geojson` | owned `System.Text.Json.JsonElement` |
| `wkt`, `ewkt` | `string` |
| `wkb`, `ewkb` | `byte[]` |
| omitted | No default; runtime `UnsupportedRepresentation` diagnostic |

These mappings depend on encoding and selected representation policy, independently
of ODCS/DPv2 provenance. The descriptor retains planar/geodetic meaning even when both
use the same coordinates and CLR type. A GeoPoint value alone cannot establish an RFC
model or CRS. DPv2 formats are source encodings, while RFC encoding is retained in
`Spatial.Encoding`, not copied into the unrelated `Format` property.

Source validation opts into a separate, offline profile:

```csharp
var options = ContractMappingOptions.Default with
{
    Odcs = new OdcsMappingOptions { EnableSpatialRfc0048 = true }
};
var report = DescriptorValidation.Validate(text, "odcs",
    DescriptorValidation.DefaultLayers, null, default, options);
```

Its profile URI is `https://seddryck.github.io/Packata/profiles/odcs-spatial-rfc0048/1.0`.
The profile extends the pinned schema in memory; default ODCS validation continues to
reject these proposed types. It validates the seven geometry subtypes, CRS, dimensions,
encoding, geography edge algorithm, bbox length, orientation and epoch. No network
schema retrieval or general extension-validation mechanism is involved.

Mapping diagnostics distinguish `SPATIAL_PROFILE` (missing opt-in), `SPATIAL_OPTIONS`
(invalid RFC options), `SPATIAL_RFC0048_CONTENT` (a general GeoJSON declaration can
include features/collections), and `SPATIAL_RFC0048_ENCODING` (TopoJSON has no RFC
encoding). The last two warn about conversion limits while retaining the DP declaration;
they do not convert payloads or manufacture a geometry. Compatibility comparison
reports changed spatial semantics as unknown until a containment policy is supplied.

### Physical storage context

`PhysicalType` stays separate from spatial meaning. `PhysicalTypeContext` retains
`Provider`, `Dialect`, and `Server`. A single bound database endpoint supplies its
provider scheme and server ID. Multiple unbound servers do not establish a context.
Offline declarations can use a field custom property:

```yaml
customProperties:
  - property: physicalTypeContext
    vendor: packata
    value: {provider: snowflake, dialect: snowflake}
```

DPv2 uses the same payload under `packata:customProperties`, alongside its existing
`x-packata-physicalType` extension. Both adapters reuse configurable binding selectors
and their disabled presets. ODCS `vendor: packata` attributes the extension; the payload's
provider identifies storage. No top-level vendor field is introduced.

An unqualified `GEOGRAPHY` does not identify a vendor. Missing/ambiguous context emits
`SPATIAL_PHYSICAL_CONTEXT`; a conflicting explicit and bound provider emits
`SPATIAL_PHYSICAL_CONTEXT_CONFLICT`. Native physical-type compatibility and SQL rendering
continue through the existing explicit target dialect and native-type registry from
[#494](https://github.com/Seddryck/Packata/issues/494); these mappers preserve source
context without adding SQL generation or treating source types as portable.

Core resolution describes the field's representation. Value decoding, owned
`JsonElement` lifetimes, and optional runtime profiles are supplied by reader bindings.
The optional NetTopologySuite profile will select the declared subtype (for example,
`Point` or `LineString`) or general `Geometry` when unspecified, through the ExtendedTypes work tracked in
[#791](https://github.com/Seddryck/Packata/issues/791). Geopoint decoding is tracked in
[#781](https://github.com/Seddryck/Packata/issues/781). Reader bindings must return the
selected CLR type consistently from `GetValue`, `GetFieldType`, and typed getters.
Unknown encodings, subtype mismatches, and TopoJSON-to-geometry conversion need explicit
diagnostics/policy; installing an optional library alone cannot justify a conversion.

The legacy `Map` API and `LegacyCompatibility` resolution retain their existing opaque
object representations for spatial fields. Strict resolution exposes the default CLR
types above, independently of reader conversion support.

## Representation limits and nested fields

Declared integer bounds must fit the selected signed/unsigned domain. Bound comparisons
use exact integers, including the full UInt64 range. Precision and scale remain available
even when the CLR decimal representation is valid. A full fixed-decimal domain fits
`System.Decimal` only when declared precision and scale are at most 28; precision 29
cannot guarantee that every coefficient fits its 96-bit significand. Invalid parameters
are errors. Binary floating-point formats with fixed-decimal parameters report loss of
exact representation. `DbType.Decimal` is independent of CLR decimal capacity; the target
database still determines its own supported precision.

`datetime` defaults to local semantics and `DateTime`; an offset pattern or
`timezone: true` selects `DateTimeOffset`. `timestamp` defaults to instant semantics;
explicit `timezone: false` selects local `DateTime`. `date`/`time` cannot retain offsets.
Temporal `precision` means fractional second digits; more than seven cannot fit CLR
temporal types, while the generic database projection remains available. Named timezone
metadata is retained with a warning because CLR temporal values store no timezone name.
Generic DbType selection does not promise a database-specific timestamp precision.

`Required` describes field presence; `Nullable` describes present values. They remain
independent for all four combinations. Readers and DataTables use underlying CLR types
with `DBNull`, not `Nullable<T>`. A future generation adapter can use the same flags to
render nullable value/reference syntax.

`Items` and `Children` are resolved recursively with paths such as `rows/items/id`.
An array with canonical items can project to a CLR array, with a database diagnostic
because DbType has no portable array representation. Explicit `object` projects to an
opaque CLR object/DbType.Object and retains its children, without a fallback diagnostic.
Unknown types are errors, even though an explicit fallback may also choose object.
Inconsistent nested shapes are diagnosed. The canonical model currently has no map
key/value fields; `map` is unsupported, rather than inventing a string key or losing
the distinction between an array and a map.

## Policies, diagnostics, and compatibility

`TypeMappingOptions.Strict` leaves unsupported projections null. Diagnostics have a code,
severity, source field path, original logical/physical type and format, and a target
(`Source`, `Runtime`, or `Database`). A source error prevents both default projections;
a target error prevents only that projection. Child diagnostics also appear on the parent.
Always inspect diagnostics even when a representation is present.

Explicit fallback policies select `Object`, `String`, or the format-independent
`LogicalType` representation. The latter uses object if the logical type is unknown.
`TypeMappingOptions.Compatible` selects `LogicalType` fallback. Each fallback has its own
diagnostic and retains the original error; selecting it deliberately accepts the risk of
representation loss. Supply `SourcePath` to root paths in an asset/document context.

Existing `Map(type, format)` and `Register` overloads remain source-compatible, with
case-sensitive lookup and their historical fallback order: exact registration, format
hook, unformatted registration, object default. In particular, legacy `Map("integer",
"u64")` still returns Int32 unless customized; rich strict resolution returns UInt64.
Both projections initialize scalar mappings from one catalog, and offset interpretation
is shared. `DbTypeMapper` remains public and injectable into `DubUrlProvisioner`.

Rich adapter resolution honors exact custom registrations before custom unformatted
registrations (the latter apply when no specific semantic representation exists), and
retains source diagnostics because registration alone does not prove the source domain
fits. Subclass registrations in `Initialize` and custom `TryMapFormat` hooks remain usable.
Custom representations carry a `CustomRegistration` diagnostic when applied.

Existing reader callers and provisioning use `TypeMappingOptions.LegacyCompatibility`.
It preserves their historical representation/conversion and temporal parsing choices,
including custom registrations, while exposing rich metadata and diagnostics to callers
of the resolver. A changed selection carries `LegacyRepresentation`. This explicit
compatibility policy can retain a representation that strict resolution rejects.
Category-label presentation is unchanged. No reader conversion or null-enforcement
change is implied by mapping metadata.

C# identifier escaping, namespaces, generated named records, DataTable construction,
and Polars/Arrow-specific representation choices belong in future target adapters.
Templates should render a prepared generation model rather than reinterpret source types.
