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
