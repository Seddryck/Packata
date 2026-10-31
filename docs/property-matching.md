---
title: Property presence and ordering
parent: Guides
permalink: /property-matching/
---

ODCS schema objects support two independent Packata boolean custom properties:

```yaml
schema:
  - name: orders
    customProperties:
      - vendor: packata
        property: allowAdditionalProperties
        value: false
      - vendor: packata
        property: enforcePropertyOrder
        value: true
    properties:
      - name: id
        logicalType: integer
        required: true
      - name: description
        logicalType: string
        required: false
```

Omission independently defaults to `allowAdditionalProperties: false` and
`enforcePropertyOrder: true`. A property's native `required` flag determines
presence; ODCS omitted/false means optional. Nullability is independent: a required
column may allow null values, and an optional column may forbid null values when present.

Additional properties are actual columns not declared in the contract.
Enforced order compares only present declared properties, ignoring permitted extras.
For declared `[id (required), name (optional), email (optional)]`, defaults accept
`[id]` and `[id,email]`, but reject `[email,id]`, `[id,country]`, and `[name]`.
With both booleans true, `[id,country,email]` passes and `[email,country,id]` fails.
With extras allowed and order disabled, `[email,country,id]` passes.
No overlap requirement is implied if every declared property is optional.

These extensions require actual boolean values. Strings such as `"false"`,
duplicates, and declarations on owners other than schema objects are errors.
The obsolete Packata `propertyMatching`/`propertyOrder` extensions produce migration
diagnostics. Other vendors remain untouched. Original declarations remain in metadata.

The canonical API replaces the former `DataSchema.FieldMatching` and the in-flight
`PropertyMatching`/`PropertyOrder` enums with `AllowAdditionalProperties` and
`EnforcePropertyOrder` booleans. `DataField.Required` retains presence and
`DataField.Nullable` retains nullability. Direct canonical construction defaults to
true/false (extras permitted, unordered); mappers explicitly apply format defaults.

To migrate old `ByName` or `Subset/Any`, use true/false and make all declared fields
required if their presence was guaranteed. `OrderedExact` or `Equal/Declared` becomes
false/true with every field required; `Exact` or `Equal/Any` becomes false/false with
every field required. `ClosedOptional` or `Superset/Any` becomes false/false with
optional fields. The former `Partial/Any` requires true/false, optional fields, and
a preserved native overlap restriction; the booleans alone are not equivalent.

| DPv2 `fieldsMatch` | AllowAdditionalProperties | EnforcePropertyOrder | Field Required |
| --- | --- | --- | --- |
| `exact` (default) | false | true | Every field |
| `equal` | false | false | Every field |
| `subset` | true | false | Every field |
| `superset` | false | false | Optional unless a native key requires presence |
| `partial` | true | false | Optional unless a native key requires presence |

Native DPv2 `partial` additionally requires at least one matching field. The mapper
retains `datapackage.fieldsMatch` metadata and structural validation enforces that
restriction; satisfaction retains Unknown for this additional native condition.
Native `constraints.required` still controls missing/null cell values independently.
DPv2 positional/name-based mapping modes are retained. No reverse DPv2 export is introduced;
mixed optionality or enforced order with extras may have no exact native equivalent.

Contract inspection exposes the booleans and per-field requiredness. Diff reports
changes to either setting and declared sequence; compatibility conservatively reports
unresolved containment as Unknown. Satisfaction considers required presence,
additional-property guarantees, and relative order independently. Unsupported semantics
remain Unknown. Category-value ordering (`ValueDomain.Ordered`) is unrelated.

Runtime column-presence, extras, and ordering checks belong to structural validation
(#526); null and other cell-value checks belong to #597. Preview and inference do not
enforce these validation requirements or infer future guarantees from a sample.

JSON Schema record export retains additional-property permission, field requiredness,
and native partial overlap. Column order applies to tabular inputs rather than JSON
object member order, and remains outside that record representation.
