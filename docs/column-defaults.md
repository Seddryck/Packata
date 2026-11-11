# SQL column defaults

Packata uses field-level custom properties to declare defaults in ODCS and Data Package v2. The database applies a default when an insert omits the column or requests `DEFAULT`. Readers keep their existing handling of missing values. A column without a declaration has no `DEFAULT` clause.

ODCS YAML uses `vendor: packata` on the column's `customProperties` entry. Other vendors' entries and entries without a vendor remain custom metadata.

```yaml
name: status
logicalType: string
customProperties:
  - vendor: packata
    property: defaultValue
    value: {type: string, value: pending}
```

Data Package v2 JSON and YAML use the field's `packata:customProperties` list.

```json
{
  "name": "status",
  "type": "string",
  "packata:customProperties": [
    { "property": "defaultValue", "value": { "type": "string", "value": "pending" } }
  ]
}
```

## Typed literals

Supply one `defaultValue` object with a case-sensitive `type` and a string `value`. Quote numeric, boolean, and temporal values in YAML. The tag must match the column's mapped database type. The selected dialect must be able to represent the value faithfully.

| Type | Value |
| --- | --- |
| `string` | Text, including an empty string and apostrophes. A value such as `CURRENT_TIMESTAMP` is quoted text. |
| `boolean` | `"true"` or `"false"`. |
| `int16`, `int32`, `int64` | Signed base-10 integer within the specified width. |
| `decimal` | Invariant base-10 number with a period as the decimal separator, within .NET decimal and column precision and scale. |
| `single`, `double` | Finite floating-point number in invariant decimal or scientific notation. |
| `date` | `YYYY-MM-DD`. |
| `time` | `HH:mm:ss`, optionally followed by 1–6 fractional digits. |
| `datetime` | `YYYY-MM-DDTHH:mm:ss`, optionally followed by 1–6 fractional digits. |
| `datetime-offset` | The same timestamp format with a required `Z` or `±HH:mm` offset. |

Explicit nulls, raw SQL, and duplicate declarations are invalid.
