---
title: Property matching and order
parent: Reference
permalink: /property-matching/
---

ODCS schema objects can opt into Packata's independent property-set and order requirements:

```yaml
customProperties:
  - vendor: packata
    property: propertyMatching
    value: equal
  - vendor: packata
    property: propertyOrder
    value: declared
```

Omission independently defaults to `equal` and `declared`. These are Packata extensions,
not standard ODCS properties. Declarations on other owners, duplicates, non-string values,
and unknown values are mapping errors. The original declarations remain in ODCS extensions.

| Matching | Declared properties relative to the input |
| --- | --- |
| `equal` | All present, no extras |
| `subset` | All present, extras allowed |
| `superset` | Some may be absent, no extras |
| `partial` | At least one present, extras and missing properties allowed |

`propertyOrder: any` ignores order. `declared` requires present declared properties in
declaration order, ignoring extras. For `[a,b,c]`, `subset` + `declared` accepts
`[a,x,b,c]`; `superset` + `declared` accepts `[a,c]` but rejects `[c,a]`.
Presence is separate from nullability of values inside a present column.

The canonical API replaces `DataSchema.FieldMatching` with typed `PropertyMatching`
and `PropertyOrder`. Direct canonical construction retains the open `Subset`/`Any`
defaults; source mappers explicitly apply their native defaults. Migrate `ByName` to
`Subset`/`Any`, `OrderedExact` to `Equal`/`Declared`, `Exact` to `Equal`/`Any`,
`ClosedOptional` to `Superset`/`Any`, and `Partial` to `Partial`/`Any`.

| DPv2 `fieldsMatch` | Canonical matching | Canonical order |
| --- | --- | --- |
| `exact` (default) | Equal | Declared |
| `equal` | Equal | Any |
| `subset` | Subset | Any |
| `superset` | Superset | Any |
| `partial` | Partial | Any |

DPv2 retains its native positional/name-based semantics. Extra combinations such as
`Subset`/`Declared` have no standard DPv2 equivalent; no reverse export is introduced.
Contract inspection includes both canonical settings. Factual diff reports changes to
either setting and declared sequence; compatibility conservatively reports unresolved
containment as Unknown. Satisfaction evaluates the dimensions independently and retains
Unknown for partial-set or unsupported extension semantics. Category-value ordering
(`ValueDomain.Ordered`) is unrelated to property order.
