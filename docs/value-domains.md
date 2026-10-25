# Shared value domains

`DataField.ValueDomain` exposes a common, typed API for ODCS enums and Data Package
inline categories/enums. `DataAsset.CategoryTable` exposes category-table metadata
when supplied by the Data Package Category Tables recipe. These types live in
`Packata.Core.Contracts` and do not reference either native format assembly.

## Catalog and effective membership

```csharp
foreach (var option in field.ValueDomain?.Entries ?? [])
    Console.WriteLine($"{option.Value.Kind}: {option.Value.Value} / {option.Label} / {option.Description}");

var allowed = field.ValueDomain?.AllowedValues;
```

`Entries` is the descriptive catalog in source sequence. `AllowedValues` is the
effective finite membership restriction, independently ordered. With DPv2
categories A/B/C and an explicit enum C/A, all three catalog entries and their
labels remain visible, while only C/A are allowed, in that sequence. ODCS enum
entries supply both the catalog and allowed set. A scalar DPv2 enum without
categories supplies entries without invented labels or descriptions.

An inline domain has entries and known allowed values. A reference domain instead
has `Reference` and null `Entries`. With a reference, `AllowedValues` is null until
finite values are known, unless the source explicitly supplies a local enum. Null
means unknown membership, not unrestricted data. A source-valid explicit empty set
has empty entries/allowed values and is not collapsed into null.

`ValueOption.Label` and `Description` are independently optional. Empty strings
remain empty. ODCS enum IDs, tags, authoritative definitions, and custom properties
are retained under each option's `Extensions["odcs"]`. Values and descriptive
metadata are detached snapshots; changing the native document cannot change them.

`ScalarValue.Kind` distinguishes string, integer, number, boolean, and null. Integer
values normalize CLR integer widths into signed 64-bit integers; numbers preserve
finite decimal, double, or float values. Equality distinguishes integer 1, number
1.0, string `"1"`, and boolean true. Equivalent finite numeric text compares equally
within the number kind without converting doubles into decimals. Number parsing
rejects unsupported range or precision rather than rounding to a convenient type.
Temporal scalar values are retained as strings, with native CLR temporal values
formatted invariantly; the owning field still supplies its logical type. Null is
available for source constraints that permit it, not for ODCS enum entries or
DPv2 categories.

## Ordering and table references

`ValueDomain.Ordered` retains field-level `categoriesOrdered`. Null is unspecified,
false is explicitly unordered, and true declares natural order. Source sequence
is preserved regardless of this flag. ODCS enum sequence does not imply natural
ordering: its flag remains null.

A reference exposes:

- `ResourceName`: the original resource name.
- `Package`: null for the current package, or a `PackageLocator` distinguishing
  `DescriptorUri` from `RegistryName` and retaining the locator text.
- `EncodedAs`: explicit value/label encoding or null when omitted.
  `EffectiveEncoding` returns value for an omitted encoding.
- `BoundAssetId`: the canonical same-package asset ID when its category-table
  descriptor and reference mapping can be bound. This never implies that its rows
  were loaded. External and unresolved references have no local bound asset ID.

`DataAsset.CategoryTable` contains `ValueField`, optional `LabelField`, and its own
nullable `Ordered` flag. A `FieldPath.Segments` sequence uses canonical logical field
names within that asset, not physical database column names. Original mapping names
remain in its source extensions. Field-level and table-level ordering are retained
independently; the common model does not invent a precedence rule.

Mapping opens no category data files and fetches no external packages. It does not
infer label or description columns by naming convention, create fake assets for
external tables, or turn unresolved references into empty membership checks.

## Compatibility and diagnostics

The existing `field.Constraints` API remains the membership interface for current
readers, provisioners, and exports. For a domain with known membership its scalar
`enum` payload is projected from `AllowedValues`, rather than maintained as an
independently editable duplicate. Labels and descriptions never appear in that
payload. Treat it as an enumerable scalar collection, not a particular mutable
array implementation.

Supplying an explicit conflicting enum via the public `DataField` API raises an
`InvalidOperationException` when `Constraints` is read. A reference with unknown
membership must also include any explicitly supplied scalar enum in its
`AllowedValues`. Existing fields without value domains keep their constraints.

Native mappers report `ODCS004` / `DP006` warnings for values outside the scalar
projection, incompatible field types, invalid subsets, invalid table descriptors,
or unbindable local references. Unsupported structured enum constraints retain
their original meaning and metadata without stringification; no scalar domain is
invented for them. Warnings do not make existing native mappings fail, but consumers
can inspect `MappingResult.Diagnostics` before provisioning. An unresolved valid
reference remains visible with unknown membership.

The old metadata paths remain available: ODCS enum snapshots under
`field.Extensions["odcs"]["enum"]`, Data Package category metadata under
`field.Extensions["datapackage"]["categories"]`, and resource field-map metadata
under `asset.Extensions["datapackage"]["categoryFieldMap"]`. These domain-related
snapshots use read-only dictionaries/lists and primitive values, which the canonical
diff API can snapshot without depending on native format classes. Code that cast
the old Data Package extension payloads to native category/reference objects should
use `field.ValueDomain` / `asset.CategoryTable` instead, or read the preserved source
property names as dictionary keys. Prefer the common API for new cross-format code.

DataField and DataAsset constructors append optional model arguments, preserving
existing source call sites. Consumers upgrading the assembly should rebuild.
See [Data Package categories](datapackage-categories.md) for standard versus recipe
syntax and the supported upstream recipe revision.
