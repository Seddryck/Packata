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

Packata's JSON and YAML readers preserve values and their logical types. Integers
use the signed 64-bit range; values outside that range fail explicitly. Quote YAML
string codes that look numeric, boolean, or null, such as `"001"` or `"true"`.
Category values and present labels must be unique. An explicit `constraints.enum`
must be a type-compatible subset of categories.

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

A reference requires `resource`. Omitted `package` means the current package.
`package` may instead be an HTTP(S) URL to a JSON package descriptor or a registry
package name. `encodedAs` may be `value` (the default when omitted) or `label`.
Omission is preserved when writing rather than replaced with an explicit default.

A category table requires `categoryFieldMap.value`; `label` and `ordered` are
optional. As with inline ordering, absent `ordered` differs from explicit `false`.
Descriptor validation checks local resource existence, tabular shape, mapped field
names, value/label types, and the required uniqueness/required declarations. Using
`encodedAs: label` requires a mapped label. The referencing field must match the
mapped **value** field's type even with `encodedAs: label`, following the merged
recipe's explicit type rule. Resource-level `ordered` and field-level
`categoriesOrdered` are retained independently; neither overwrites the other.

Validation checks descriptors only: it does not open category CSVs, inspect rows,
or fetch external packages. External references are retained as metadata and are
never mistaken for local resources. Registry resolution, automatic downloading,
value expansion, and generating DDL enum constraints from category table rows are
not implemented. Explicit enum constraints remain available as declared.

Canonical field extensions retain the reference object under
`extensions["datapackage"]["categories"]`; resource extensions retain
`categoryFieldMap`. References are not converted into empty inline categories.

## Native model and converter round trips

`Field.Categories` is a `CategoryCollection` containing either inline entries or a
`CategoryTableReference` available through `Reference`. Inline collections support
collection expressions and assignment from `List<ICategory>`. Adding inline entries
to a reference collection is rejected.

`ICategory.Value` is a string or `long`, independently of its optional `Label`.
`Category` represents object-form entries, `CategoryLabel` scalar strings, and
`CategoryInteger` scalar integers. Existing `CategoryLabel.Label` access remains
available. Code that previously assumed `Category.Value` was `int`, or that
`Field.Categories` was a mutable `List<ICategory>`, must use the typed value or
collection API instead.

The JSON/YAML category and metadata converters support read/write/read round trips
for inline categories, references, and field maps, including absent optional
metadata. This does not add a general-purpose Data Package descriptor writer.
