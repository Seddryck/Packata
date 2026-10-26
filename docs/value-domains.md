---
title: Value domains
parent: Concepts
nav_order: 2
nav_exclude: false
permalink: /value-domains/
---

# Value domains

A field sometimes draws its values from a known set. For example, an order's
`status` might use `new`, `paid` and `cancelled`. The contract can give those codes
labels and descriptions so people understand them, and can restrict which codes
are allowed in the data. Packata calls this information a **value domain**.

ODCS enums and Data Package enums/categories express this information differently.
Packata maps them to a shared representation so applications can inspect the
codes, their descriptions and any allowed-value restriction in the same way.

## Catalog and effective membership

A catalog describes values; a restriction determines which values are allowed.
These need not contain the same set.

Suppose a category catalog contains `A`, `B` and `C`, each with a label, but a
particular field's explicit enum allows only `C` and `A`. Packata keeps all three
catalog entries for description, while retaining only `C` and `A` as the allowed
values, in that order.

| Information | Shared model property | Result in this example |
| --- | --- | --- |
| Descriptive catalog | `Entries` | `A`, `B`, `C`, with their labels and descriptions |
| Allowed values | `AllowedValues` | `C`, `A` |

ODCS enum entries provide both the catalog and its allowed values. A Data Package
enum without categories also supplies entries, but Packata does not invent labels
or descriptions for them.

A label and a description are independently optional. Either can be missing,
and an explicitly empty string stays empty. Additional ODCS enum information,
such as IDs, tags, authoritative definitions and custom properties, is retained
in each option's `Extensions["odcs"]`.

### Distinguish unknown values from an empty set

A contract can refer to a separate category table instead of listing every value
inline. For example, `country_code` could refer to a `countries` resource.
Packata preserves that reference without reading the table's rows.

For a reference, `Entries` is null because no inline catalog was supplied.
`AllowedValues` is also null unless the source separately provides a local enum
that establishes the allowed values. Null here means **unknown membership**;
it does not mean every value is allowed.

An explicitly empty set that is valid in its source standard stays an empty
collection. It is different from an unknown set and is not converted to null.

## Ordering and table references

A list's source order is always preserved, but that does not necessarily mean
the values have a natural order. For instance, a severity catalog may deliberately
run from low to high, while a country catalog may simply be listed alphabetically.

`ValueDomain.Ordered` preserves Data Package's `categoriesOrdered`: true declares
natural order, false explicitly declares no natural order, and null means it was
not specified. An ODCS enum's sequence alone does not declare natural order, so
its flag remains null.

### Understand a reference to a category table

A reference records which resource supplies the categories and how the field uses
it. It can point to another resource in the same package or to an external package.

| Property | Meaning |
| --- | --- |
| `ResourceName` | The resource name as written in the source |
| `Package` | Null for this package; otherwise a `PackageLocator` retaining a descriptor URI or registry name and its locator text |
| `EncodedAs` | Whether the field contains category values or labels, when explicitly declared |
| `EffectiveEncoding` | Uses value encoding when `EncodedAs` was omitted |
| `BoundAssetId` | The shared-model asset ID when a same-package reference can be matched to a category-table descriptor |

A matched `BoundAssetId` means Packata found the table's description. It does not
mean it opened the table or established its complete set of values. External and
unresolved references have no local bound asset ID.

`DataAsset.CategoryTable` describes the table itself: its `ValueField`, optional
`LabelField` and nullable `Ordered` flag. A field path uses logical field names
inside that asset, stored in `FieldPath.Segments`, rather than physical database
column names. Original mapping names remain in source extensions.

The field and the category table can each declare ordering. Packata retains both
without choosing which takes precedence. Mapping does not fetch external
packages, read category rows, guess label/description columns from their names,
or create placeholder assets for external tables. An unresolved reference stays
unresolved rather than becoming an empty allowed set.

## Use the shared model in .NET

`DataField.ValueDomain` exposes the domain. `DataAsset.CategoryTable` exposes
category-table metadata when a Data Package Category Tables recipe supplies it.
Both live in `Packata.Core.Contracts`, without dependencies on either native
format assembly.

For a field whose domain is already mapped, you can inspect its catalog and
restriction separately:

```csharp
foreach (var option in field.ValueDomain?.Entries ?? [])
    Console.WriteLine($"{option.Value.Kind}: {option.Value.Value} / {option.Label} / {option.Description}");

var allowed = field.ValueDomain?.AllowedValues;
```

The loop prints each catalog entry's typed value, label and description. `allowed`
contains the known restriction or is null when membership is unknown. The mapped
values and descriptive metadata are detached snapshots: later edits to the
native document do not change them.

### Keep value types distinct

`ScalarValue.Kind` identifies strings, integers, numbers, booleans and null.
Integer `1`, number `1.0`, string `"1"` and Boolean `true` remain different values;
labels do not change those types.

CLR integer widths normalize to signed 64-bit integers. Number values retain
finite decimal, double or float representations. Equivalent finite numeric text
compares equally within the number kind without converting doubles to decimals.
Unsupported range or precision is rejected rather than rounded.

Temporal values remain strings, with CLR temporal values formatted invariantly;
the owning field still declares the logical type. Null is included only where
the source permits it. ODCS enum entries and Data Package categories do not
permit null.

## Compatibility and diagnostics

Existing readers, provisioners and exports obtain enum restrictions through
`field.Constraints`. When a domain has known allowed values, Packata derives the
scalar `enum` constraint from `AllowedValues`. Labels and descriptions are not
part of that constraint, and there is no second independently editable copy.
Consume the payload as an enumerable collection of scalar values rather than
assuming a mutable array implementation.

If application code supplies a conflicting enum through the public `DataField`
API, reading `Constraints` throws `InvalidOperationException`. A reference with
unknown membership must also represent any explicitly supplied scalar enum in
`AllowedValues`. Fields without value domains keep their existing constraints.

### Review mapping warnings

Native mapping reports `ODCS004` or `DP006` when it cannot create a valid scalar
domain. Causes include incompatible field types, invalid subsets, invalid table
descriptors and local references that cannot be bound.

Structured enum constraints that cannot be represented as scalar domains retain
their original meaning and metadata. Packata does not convert them to strings
or invent a replacement domain. These warnings do not make the native mapping
fail, but review `MappingResult.Diagnostics` before using the result to provision
a database. A valid reference can also remain visible with unknown membership.

### Read preserved source metadata when needed

The common API is the preferred entry point for new code supporting both formats.
Source snapshots are still available at:

- `field.Extensions["odcs"]["enum"]` for ODCS enum metadata.
- `field.Extensions["datapackage"]["categories"]` for Data Package categories.
- `asset.Extensions["datapackage"]["categoryFieldMap"]` for the resource field map.

These contain read-only dictionaries, lists and primitive values, so diff can
snapshot them without native format classes. Code that previously cast those
Data Package extension values to native category/reference objects should use
`field.ValueDomain` and `asset.CategoryTable` instead, or read preserved property
names as dictionary keys.

The `DataField` and `DataAsset` constructors add optional arguments at the end,
preserving existing source call sites; rebuild consumers when upgrading the
assembly. See [Data Package categories]({{ '/datapackage-categories/' | relative_url }})
for standard and recipe syntax and the supported recipe revision.
