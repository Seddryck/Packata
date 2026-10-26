---
title: Contracts and standards
nav_order: 5
nav_exclude: false
has_children: true
permalink: /standards/
---

Packata supports Data Package v2 and ODCS 3.2.0 through distinct native models. Both map into one canonical contract. Support at one layer does not guarantee support at every layer.

| Layer | Data Package v2 | ODCS 3.2.0 |
| --- | --- | --- |
| Native input | JSON and YAML | YAML |
| Descriptor validation | Descriptor, resources, fields, licenses/contributors, structured constraints and Table Dialect | YAML/semantic schema constraints; CLI also checks identity, version, status, kind and API version |
| Canonical mapping | Resources, schemas, keys/relationships, endpoints, metadata and namespaced extensions | Schema/properties, servers, relationships, governance, custom metadata, SLA/lifecycle commitments |
| Execution | Registered readers and provisioners operate on mapped capabilities | Same shared stack; ambiguous multiple servers do not gain guessed bindings |

## Data Package v2

The Data Package model retains field constraints, primary, unique and foreign keys,
categories and dialects. It also supports resource kinds, metrics and inline dictionary-record
data in JSON and YAML.

Not every source concept becomes a canonical constraint. A category-table reference does not
automatically load values, and a metric definition does not cause a metric to be evaluated.
Keep the native document when your workflow needs source-only metadata.

## ODCS

The ODCS model retains context blocks, schema and server variants, nested logical types,
temporal options and relationships. It also retains teams, roles, pricing, support channels,
custom properties and SLA metadata.

Packata recognizes explicit `vendor: packata` category-table declarations in custom
properties and maps them to shared category references and lookup metadata. This
convention extends ODCS; it does not infer labels from foreign keys. See the
[ODCS category-table example and binding rules]({{ '/guides/data/' | relative_url }}#referenced-category-tables-in-odcs).

Quality definitions can be textual, library-based, SQL-based or custom, and can include
schedules. Reading or preserving them does not execute the rules or schedules. The current
canonical mapping does not populate native ODCS quality rules, so their presence in YAML
does not establish runtime validation.

SLA and lifecycle metadata **are** normalized into canonical commitments. Unknown/invalid SLA properties remain in extensions with diagnostics. See [commitments and temporal values]({{ '/concepts/commitments/' | relative_url }}).

## Export boundaries

A JSON Schema export describes one record using Draft 2020-12. Required properties and
nullability remain independent. Conversion fails for unsupported logical types, invalid
constraints or constraints that require evaluating the whole dataset. Physical storage,
governance and quality execution are outside that representation.

A CSV dialect export instead describes how to parse an endpoint's data. It does not scan
the CSV. See the [contract tasks]({{ '/guides/contracts/' | relative_url }}) and
[dialect export guide]({{ '/csv-dialect-export/' | relative_url }}) for examples.

## Categories, value domains and consumer compatibility

Read [Data Package categories]({{ '/datapackage-categories/' | relative_url }}) for standard
inline values, the separately versioned Category Tables recipe, descriptor checks and lookup
limitations.

The [shared value-domain guide]({{ '/value-domains/' | relative_url }}) explains how canonical
values retain ODCS enum labels and descriptions, typed scalars, allowed membership, ordering
and references. Mapping reports diagnostics without loading category rows.

[Contract compatibility]({{ '/compatibility/' | relative_url }}) is available for same-standard ODCS 3.2.0 and Data Package v2 inputs. Coverage warnings and unsupported source constructs produce explicit unknown findings; cross-standard analysis fails. Source-native preservation is distinct from an established compatibility guarantee.
