---
title: Data Package categories
parent: Contracts and standards
nav_order: 1
nav_exclude: false
permalink: /datapackage-categories/
---

# Data Package categories

## Standard DPv2 inline categories

The [DPv2.0 Table Schema standard](https://datapackage.org/standard/table-schema/#categories)
allows string and integer fields to define categories inline, as either scalar values
or objects with a required `value` and an optional string `label`:

```json
{"name":"fruit","type":"string","categories":["001","A"]}
```

```json
{"name":"rating","type":"integer","categories":[{"value":1,"label":"Low"},{"value":2}],"categoriesOrdered":true}
```

Packata preserves category values and their logical types when reading JSON or YAML.
Integer values must fit the signed 64-bit range; larger values fail explicitly. In YAML,
quote string codes such as `"001"` or `"true"` so they are not interpreted as numbers,
booleans or null.

Category values and any supplied labels must be unique. When a field also declares
`constraints.enum`, that enum must be a type-compatible subset of the categories.

An absent `categoriesOrdered` remains null and makes no ordering assumption;
`false` and `true` remain distinct. Category sequence is preserved. Canonical mapping
retains category metadata and creates an enum constraint from nonempty inline
categories when no explicit enum constraint exists.

## Values, labels and opt-in decoding

The value is the code stored in a row; its optional label is display text. Mapping
does not replace that code or change the field's declared logical type. Read the
shared catalog through `DataField.ValueDomain.Entries`: each `ValueOption` has a
typed `ScalarValue` in `Value` and an optional `Label`. `AllowedValues` keeps the
effective restriction independently, including an explicit `constraints.enum`
subset. `ValueDomain.Ordered` preserves true, false and unspecified ordering.

The same API represents ODCS enum values, including strings, integers, numbers
and booleans. ODCS descriptions appear in `ValueOption.Description`; IDs, tags,
custom properties and authoritative definitions remain in `Extensions["odcs"]`.
See the [canonical API examples]({{ '/quickstart/dotnet/' | relative_url }}#read-category-labels-from-either-standard).

To display labels, enable `ResourceReaderFactoryOptions.DecodeCategories` in .NET
or pass `--decode-categories` to `packata data preview`. This option is disabled
by default and supports only the PocketCsvReader delimited engine (CSV/TSV/PSV).
Other engines return an explicit unsupported-engine diagnostic. Local category
tables from the Category Tables recipe can also supply the labels, as described below.

A matching code with a label returns that string. Unlabeled entries, unmatched
code text and nulls remain unchanged; an empty label remains an empty string.
Packata registers native PocketCsvReader `WithSequence` mappings. Matching is
case-sensitive against exact unquoted CSV text, using invariant code text
(`1`, `1.5`, `true`). Quoted or escaped codes and alternate spellings such as
`01` remain unchanged. Columns with any labels return `string` for both labels
and fallback codes; columns without labels retain their original type.
`GetFieldType` reports `typeof(string)` and `GetDataTypeName` reports `string`
for labeled columns. PocketCsvReader does not implement `GetSchemaTable()`.

**Write category-code cells that should be decoded as unquoted, unescaped CSV
text.** PocketCsvReader skips sequence replacement for fields it marks as quoted
or escaped, even if their parsed text matches a category code. For example, write
`S`, not `"S"`, to decode it to `South`. Codes that require CSV quoting or escaping
cannot be decoded by this native feature. This requirement concerns cells in the
CSV data; string values in JSON/YAML descriptors still use their normal syntax.

Decoding does not edit source descriptors or data, change the canonical logical
type, or cause validation to compare labels against code restrictions. Use a
default reader when validating original row values. The
[data guide]({{ '/guides/data/' | relative_url }}#preview-category-labels) contains
complete ODCS and DPv2 descriptors, commands and output with and without decoding.

## Category Tables recipe

The resource-reference form is supported separately from the standard DPv2.0
inline forms. The implementation follows the [Category Tables recipe merged in
upstream PR #1081](https://github.com/frictionlessdata/datapackage/blob/3b9efb7b0fa08f08b0d55d2ba86b2be271423f94/content/docs/recipes/category-tables.md).
This identifies the recipe revision supported by Packata; it does not claim that
resource references are part of the published DPv2.0 standard.

For schema validation, opt in with the Packata Category Tables 1.0.0 `$schema`
shown below. Its [published profile](https://raw.githubusercontent.com/Seddryck/Packata/profile-category-tables-v1.0.0/docs/profiles/category-tables/1.0.0/datapackage.json)
has the same URI as its `$id`. Packata embeds those exact bytes and resolves the
profile offline in the library, CLI and VS Code. The official
`https://datapackage.org/profiles/2.0/datapackage.json` still rejects reference
objects; omitting `$schema` selects that strict official profile.

```json
{
  "$schema": "https://raw.githubusercontent.com/Seddryck/Packata/profile-category-tables-v1.0.0/docs/profiles/category-tables/1.0.0/datapackage.json",
  "resources": [
    {
      "name": "survey",
      "type": "table",
      "path": "survey.csv",
      "schema": {
        "fields": [
          {"name":"fruit","type":"string","categories":{"resource":"fruit-codes"}}
        ]
      }
    },
    {
      "name": "fruit-codes",
      "type": "table",
      "path": "fruit-codes.csv",
      "categoryFieldMap": {"value":"code","label":"name","ordered":false},
      "schema": {
        "fields": [
          {"name":"code","type":"string","constraints":{"required":true,"unique":true}},
          {"name":"name","type":"string","constraints":{"unique":true}}
        ]
      }
    }
  ]
}
```

A category reference must name its `resource`. If `package` is omitted, Packata treats the
resource as belonging to the current package. An external package can instead be identified
by an HTTP(S) URL to a JSON descriptor or by a registry package name.

The reference can select value or label encoding with `encodedAs`. Value encoding is the
default when it is omitted. Writing preserves that omission rather than inserting an
explicit default.

A category table must declare `categoryFieldMap.value`. Its label field and ordering flag
are optional; an omitted `ordered` remains distinct from explicit false.

Descriptor validation checks that a local resource exists, is tabular, and declares the mapped
fields with suitable types and the required uniqueness/required constraints. Label encoding
requires a mapped label field.

The referencing field must match the mapped **value** field's type even with `encodedAs: label`.
This follows the merged recipe's type rule. Resource-level `ordered` and field-level
`categoriesOrdered` remain independent; neither overwrites the other.

These checks examine descriptors only. They do not open category CSVs, inspect rows or fetch
external packages. External references remain metadata and are not mistaken for local resources.

With `--decode-categories`, preview resolves the explicitly named local category resource
in either resource order, reads its mapped value and optional label columns, and applies
native PocketCsvReader sequences. A foreign key alone never supplies labels. Lookup paths
are resolved relative to the descriptor, just like the consuming data paths. Omitted
`encodedAs` means `value`; `encodedAs: label` bypasses lookup loading and replacement.
Without decoding, no lookup is opened solely to obtain labels.

In .NET, supply the containing canonical contract so references can resolve:

```csharp
var factory = ResourceReaderFactory.Create(options => options.DecodeCategories = true);
using var reader = await factory.OpenAsync(new DataEndpointReadRequest(
    endpoint, asset.Schema, Contract: contract), cancellationToken);
```

Use a stream resolver for descriptor-relative paths (the CLI supplies one). Each reader
opening loads a shared lookup only once and releases lookup streams/readers before opening
the consuming reader. No lookup cache survives the operation. Loading honors cancellation
and is bounded across all lookups by `CategoryTableRowLimit` (100,000 rows) and
`CategoryTableByteLimit` (16 MiB of CSV input) in `ResourceReaderFactoryOptions`. Exceeding
either limit fails explicitly. Required codes must be present and unique; non-null labels
must be unique, including empty strings. Integer codes must fit the signed 64-bit range.
Missing label mappings retain codes; an empty label produces an empty string. The native
matching, fallback and returned-type rules above also apply to table labels.

### Select a language or a label column

Packata extends category-table metadata with `categoryFieldMap.labels`, a list of
eligible logical label fields, and field-level `language` tags. These additions
are Packata extensions, not part of the published Data Package standard or the
upstream Category Tables recipe. The existing `label` remains the default and is
always eligible, even when `labels` is omitted.

```yaml
categoryFieldMap:
  value: value
  label: label_en
  labels: [label_en, libelle, label_de, short_label]
schema:
  fields:
    - name: value
      type: string
      constraints: {required: true, unique: true}
    - name: label_en
      type: string
      language: en
      constraints: {unique: true}
    - name: libelle
      type: string
      language: fr
      missingValues: ['~']
    - name: label_de
      type: string
      language: de
    - name: short_label
      type: string
```

For lookup rows `N,North,Nord,Norden,N.` and `S,South,Sud,Süden,S.`:

```console
packata data preview datapackage.json --asset survey --decode-categories-language fr
packata data preview datapackage.json --asset survey --decode-categories-label label_de
packata data preview datapackage.json --asset survey --decode-categories-label short_label
```

These display `Nord`/`Sud`, `Norden`/`Süden`, and `N.`/`S.` respectively. Either
selector enables decoding; adding `--decode-categories` is allowed. The language
and label selectors are mutually exclusive. Without a selector, decoding uses
the default label as before.

Language selection uses case-insensitive tags: exact tag, primary language,
default label, then original code. `fr-BE` tries `fr-BE`, `fr`, then the default;
`zh-Hant-TW` tries that exact tag then `zh`, without intermediate tags. The machine's
locale is never used. Ordinary language-tag syntax supports language, optional
script/region/variants/extensions/private use. Registry membership, grandfathered
tags and private-use-only tags are not supported. `label_de` is a column name,
not a language tag.

The same fallback order applies to null translations in each row. For CSV, declare
a field-level `missingValues` marker such as `~` above. Empty cells remain empty
strings unless explicitly declared missing; an empty label stops fallback.
Unmatched codes and null source values retain existing decoding behavior. If a
table has neither an exact nor a primary-language mapping, the CLI emits one
warning for that table per reader opening, rather than warning for every row.

Explicit selection requires an exact, case-sensitive declared logical label field
in every involved table. It never selects another column; a null selected label
retains the original code. Language selection lets different tables use different
column names. Multiple eligible columns matching a language cause an actionable
ambiguity error suggesting `--decode-categories-label`. A language on a description
alone does not make it a label, and column names do not imply languages.

All label fields must be strings. The default label retains the recipe's unique
constraint and runtime non-null uniqueness checks. Additional display labels may
repeat text; they are not used to reverse-map label-encoded input. Inputs with
`encodedAs: label` bypass selection. Source files, code identity, validation and
ordering are unaffected. Inline decoding keeps its existing single-label behavior.

The equivalent .NET configuration is:

```csharp
var factory = ResourceReaderFactory.Create(options =>
{
    options.DecodeCategoriesLanguage = "fr"; // Enables decoding.
    options.CategoryDecodingDiagnostic = message => Console.Error.WriteLine(message);
});
// Alternatively set options.DecodeCategoriesLabel = "short_label".
using var reader = await factory.OpenAsync(new DataEndpointReadRequest(
    endpoint, asset.Schema, Contract: contract), cancellationToken);
```

Only local, uncompressed delimited lookup resources using PocketCsvReader are supported.
External-package references, remote lookup paths, other lookup engines, compressed lookups
and cyclic references fail with explicit diagnostics when resolution is required. Lookups
are read without category decoding; nested category references never trigger recursive
reader initialization.

Registry resolution, automatic downloading and generation of DDL enum constraints from
table rows are not implemented. Explicit enum constraints remain available as declared.

The [shared value-domain model]({{ '/value-domains/' | relative_url }}) exposes inline values
and optional labels through `DataField.ValueDomain`, and table mappings through
`DataAsset.CategoryTable`.

The field's `extensions["datapackage"]["categories"]` also retains a detached reference
object, while resource extensions retain `categoryFieldMap`. A reference is not converted
into an empty inline category list.

## Native model and converter round trips

`Field.Categories` is a `CategoryCollection` containing either inline entries or a
`CategoryTableReference` available through `Reference`. Inline collections support
collection expressions and assignment from `List<ICategory>`. Adding inline entries
to a reference collection is rejected.

Read the typed value from `ICategory.Value`, which is a string or `long`, independently of
the optional `Label`. Object-form entries use `Category`, scalar strings use `CategoryLabel`,
and scalar integers use `CategoryInteger`. Existing `CategoryLabel.Label` access remains available.

Code that assumed `Category.Value` was an `int`, or `Field.Categories` was a mutable
`List<ICategory>`, must use the typed value or collection API instead.

The JSON/YAML category and metadata converters support read/write/read round trips
for inline categories, references, and field maps, including absent optional
metadata. This does not add a general-purpose Data Package descriptor writer.
