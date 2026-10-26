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

## Category Tables recipe

The resource-reference form is supported separately from the standard DPv2.0
inline forms. The implementation follows the [Category Tables recipe merged in
upstream PR #1081](https://github.com/frictionlessdata/datapackage/blob/3b9efb7b0fa08f08b0d55d2ba86b2be271423f94/content/docs/recipes/category-tables.md).
This identifies the recipe revision supported by Packata; it does not claim that
resource references are part of the published DPv2.0 standard.

```json
{
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

Registry resolution, automatic downloading, value expansion and generation of DDL enum
constraints from table rows are not implemented. Explicit enum constraints remain available
as declared.

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
