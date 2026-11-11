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

## Temporal and sequence expressions

Use `defaultExpression` in the same custom property list. Each column can declare one default.

```yaml
name: invoiceNumber
logicalType: integer
customProperties:
  - vendor: packata
    property: defaultExpression
    value:
      kind: next-sequence-value
      sequence: {schema: billing, name: invoice_numbers}
```

| Kind | Destination and result |
| --- | --- |
| `current-timestamp` | Timestamp in the inserting session's time zone. An offset-bearing destination receives the session offset. |
| `current-utc-timestamp` | UTC timestamp. An offset-bearing destination receives offset zero. |
| `current-date` | Calendar date in the inserting session. |
| `current-time` | Time of day in the inserting session. |
| `next-sequence-value` | Next value from a named sequence for a signed integer or an exact decimal with room for an integral part. |

Timestamp and time kinds accept an optional integer `fractionalSecondsPrecision` from 0 through 9. Omitting it uses the database default precision. The selected provider must support the requested kind, precision, destination type, and qualification.

A sequence requires a nonempty `name` and at least one of `schema`, `database`, or `catalog`. Each component is an identifier. Create the sequence and grant access before deploying the table. The sequence remains under its owner's control when the table is dropped.

## User identity and UUID expressions

| Kind | Result and destination |
| --- | --- |
| `current-user` | Effective database authorization identity. The text length must meet the provider's identity capacity. |
| `session-user` | Session authorization identity. The text length must meet the provider's identity capacity. |
| `new-uuid` | A 36-character hyphenated UUID. Requires `representation: canonical-text` and either variable-width text of at least 36 characters or fixed-width text of exactly 36 characters. |

The identity kinds accept only `kind`. Packata uses the physical SQL length when declared, otherwise it uses the field's string `maxLength`. Select a provider that supports the expression on the resolved column type.

```json
{
  "name": "id",
  "type": "string",
  "constraints": { "maxLength": 36 },
  "packata:customProperties": [
    { "property": "defaultExpression", "value": { "kind": "new-uuid", "representation": "canonical-text" } }
  ]
}
```
