---
title: Custom-property mapping bindings
parent: Reference
permalink: /custom-property-bindings/
---

# Custom-property mapping bindings

A binding selects the source custom property for an existing canonical feature. An override replaces the built-in selector. Packata preserves both declarations in source metadata if a document includes both. The selected declaration uses the same payload validation and fallback behavior as the built-in declaration.

## Use datacontract-cli's additionalProperties

Given this ODCS schema object, configure Packata to read the unvendored `additionalProperties` declaration.

```yaml
schema:
  - name: orders
    customProperties:
      - property: additionalProperties
        value: false
```

```sh
packata config set mapping.odcs.schema.allowAdditionalProperties '{"property":"additionalProperties","vendor":null}'
packata config get mapping.odcs.schema.allowAdditionalProperties --output-format json
```

This maps to `DataSchema.AllowAdditionalProperties = false`, even if the same schema also declares `packata/allowAdditionalProperties: true`. The YAML boolean `false` is required. The quoted string `"false"` is invalid.

For an ODCS setting, supply a JSON object with `property`, `vendor`, or both. Use a non-empty string for a supplied property or a named vendor. Omit a member to keep its built-in value. Set `vendor` to JSON `null` to match only unvendored declarations. CLI and the language server use the same existing Packata user configuration file, including the `PACKATA_CONFIG_HOME` directory override.

```sh
packata config set mapping.datapackage.field.defaultValue initial
packata config list
packata config unset mapping.odcs.schema.allowAdditionalProperties
```

`get` and `list` show the effective selector and built-in value. `set` replaces the saved override, and `unset` restores the built-in selector. Configuration rejects unknown keys and selectors that match the same source property and vendor on the same owner. For Data Package v2, supply the replacement property name as a string.

## C# adapter options

```csharp
var options = new OdcsMappingOptions
{
    Schema = new OdcsSchemaBindings
    {
        AllowAdditionalProperties =
            OdcsBindingOverride.Unvendored("additionalProperties")
    }
};

var result = odcsDocument.ToCanonicalContract(options);
```

Use `UseBuiltInVendor(property)` to change the name and keep the built-in vendor, or `ForVendor(vendor, property)` to select a named vendor. Omitting the property argument keeps the built-in name. `DataPackageMappingOptions` exposes `DefaultValueProperty` and `DefaultExpressionProperty`. The mappers also accept these options in their constructors. The no-options entry points use the built-in catalog.

## ODCS bindings

Prefix these IDs with `mapping.odcs.`. The built-in vendor is `packata`, and the built-in property is the last segment of the ID. Schema and field selectors use case-sensitive matching. Server selectors use case-insensitive matching.

| Binding ID | Source owner | Payload and behavior when absent |
| --- | --- | --- |
| `schema.allowAdditionalProperties` | Schema object | Boolean. Defaults to `false`. |
| `schema.enforcePropertyOrder` | Schema object | Boolean. Defaults to `true`. |
| `schema.categoryFieldMap` | Schema object | Table column names as `value` and optional `label`, with optional boolean `ordered`. Absent means no category table declaration. |
| `field.categories` | Schema property | A category-table reference with `resource` and optional `package` and `encodedAs` (`value` or `label`). Conflicts with inline enum. Absent means no category-table reference. |
| `field.defaultValue` | Schema property | Typed literal object with string `type` and `value`. Conflicts with a default expression. Absent means no literal default. |
| `field.defaultExpression` | Schema property | Expression object with `kind` and the members supported for that kind. Conflicts with a default value. Absent means no expression default. |
| `server.delimiter` | Server | Delimited readers use a single character. Absent keeps any native delimiter or the existing reader defaults. |
| `server.quoteChar` | Server | Delimited readers use a single character. Absent keeps the reader's default. |
| `server.lineTerminator` | Server | Delimited readers use a line ending string. Absent keeps the reader's default. |
| `server.header` | Server | Boolean header setting. Absent keeps the existing reader behavior. |
| `server.sheetName` | Server | Spreadsheet sheet name. Conflicts with a sheet number. Absent leaves selection to the sheet number or reader default. |
| `server.sheetNumber` | Server | One-based spreadsheet sheet number. Conflicts with a sheet name. Absent leaves selection to the sheet name or reader default. |
| `server.headerRows` | Server | A one-based spreadsheet row number in a row list. The reader supports one header row. Absent lets the reader derive header rows from the header setting. |
| `server.commentRows` | Server | A list of one-based, contiguous leading spreadsheet row numbers. Absent means no configured comment rows. |

Server bindings map options for CSV, TSV, PSV, XLS and XLSX. Each reader applies the options it supports. A matching custom delimiter takes precedence over the native delimiter, and the last matching server declaration wins. Schema booleans, category declarations, and column defaults keep their existing duplicate-declaration checks. See [SQL column defaults]({{ '/column-defaults/' | relative_url }}) for the literal types and expression kinds.

## Data Package v2 bindings

Prefix these IDs with `mapping.datapackage.`. Entries are read from each field's `packata:customProperties` list.

| Binding ID | Source owner | Built-in property | Payload and behavior when absent |
| --- | --- | --- | --- |
| `field.defaultValue` | Field | `defaultValue` | Typed literal object with string `type` and `value`. Conflicts with a default expression. Absent means no literal default. |
| `field.defaultExpression` | Field | `defaultExpression` | Expression object with `kind` and the members supported for that kind. Conflicts with a default value. Absent means no expression default. |

Duplicate defaults are diagnosed. A field with neither kind of default produces no SQL `DEFAULT` clause.
