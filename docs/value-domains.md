---
title: Shared value domains
parent: Concepts
nav_order: 2
nav_exclude: false
permalink: /value-domains/
---

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

A domain separates its descriptive catalog from the values that are allowed. `Entries`
keeps the catalog in source order, while `AllowedValues` carries the effective finite
membership restriction in its own order.

For example, DPv2 categories A/B/C with an explicit enum C/A retain all three catalog entries
and their labels, but allow only C/A in that sequence. ODCS enum entries supply both lists.
A scalar DPv2 enum without categories supplies entries without inventing labels or descriptions.

An inline domain provides entries and known allowed values. A reference domain instead
provides `Reference` and leaves `Entries` null. Its `AllowedValues` also remains null until
finite membership is known, unless the source supplies a local enum.

Here, null means that membership is unknown, not that any value is allowed. An explicit empty
set that is valid in its source standard remains an empty entries/allowed-values collection;
it is not collapsed into null.

Each value can have a label, a description, both or neither. `ValueOption.Label` and
`Description` are independently optional, and an empty string remains an empty string.
ODCS enum IDs, tags, authoritative definitions and custom properties are retained in the
option's `Extensions["odcs"]`.

Both the value and its descriptive metadata are detached snapshots. Changing the native
document later cannot change the mapped result.

`ScalarValue.Kind` distinguishes strings, integers, numbers, booleans and null. Integer
values are normalized from CLR integer widths to signed 64-bit integers. Number values retain
finite decimal, double or float representations.

Equality keeps those kinds distinct: integer 1, number 1.0, string `"1"` and boolean true
are different values. Equivalent finite numeric text compares equally within the number kind,
without converting doubles to decimals. Parsing rejects unsupported range or precision rather
than rounding to a convenient type.

Temporal scalars remain strings, with native CLR temporal values formatted invariantly.
The owning field still declares the logical type. Null is available only where source
constraints permit it; ODCS enum entries and DPv2 categories do not permit null.

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

For a category table, `DataAsset.CategoryTable` identifies the `ValueField`, an optional
`LabelField` and the table's own nullable `Ordered` flag. Paths in `FieldPath.Segments`
use logical canonical field names within that asset, not physical database column names.
The original mapping names remain in source extensions.

Field ordering and table ordering are retained independently. The shared model does not choose
one as taking precedence over the other.

Mapping opens no category data files and fetches no external packages. It does not
infer label or description columns by naming convention, create fake assets for
external tables, or turn unresolved references into empty membership checks.

## Compatibility and diagnostics

Readers, provisioners and exports continue to obtain membership constraints through
`field.Constraints`. When a domain has known membership, the scalar `enum` payload is
derived from `AllowedValues` rather than kept as a separately editable copy.

Labels and descriptions do not enter that payload. Consume it as an enumerable collection
of scalar values, without depending on a particular mutable array implementation.

Supplying an explicit conflicting enum via the public `DataField` API raises an
`InvalidOperationException` when `Constraints` is read. A reference with unknown
membership must also include any explicitly supplied scalar enum in its
`AllowedValues`. Existing fields without value domains keep their constraints.

Native mapping reports `ODCS004` or `DP006` warnings when it cannot create a valid scalar
projection. Causes include incompatible field types, invalid subsets or table descriptors,
and local references that cannot be bound.

Structured enum constraints that are not supported as scalar domains retain their meaning
and metadata. They are not converted to strings or replaced with an invented scalar domain.
These warnings do not make an existing native mapping fail, but you should inspect
`MappingResult.Diagnostics` before provisioning. A valid unresolved reference remains visible
with unknown membership.

You can still read ODCS enum snapshots at `field.Extensions["odcs"]["enum"]`,
Data Package categories at `field.Extensions["datapackage"]["categories"]`, and the resource
field map at `asset.Extensions["datapackage"]["categoryFieldMap"]`.

These snapshots contain read-only dictionaries, lists and primitive values. The canonical
diff can snapshot them without depending on native format classes. If existing code casts
Data Package extension values to native category or reference objects, use `field.ValueDomain`
and `asset.CategoryTable` instead, or read the preserved property names as dictionary keys.
Prefer the common API for new code that handles both formats.

DataField and DataAsset constructors append optional model arguments, preserving
existing source call sites. Consumers upgrading the assembly should rebuild.
See [Data Package categories]({{ '/datapackage-categories/' | relative_url }}) for standard versus recipe
syntax and the supported upstream recipe revision.
