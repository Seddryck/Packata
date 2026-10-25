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

The native model includes structured field constraints, primary/unique/foreign keys, categories, dialects, resource kinds, metrics and inline dictionary-record data. JSON/YAML serialization preserves source-standard concepts that do not all become canonical constraints. Category table references and metric definitions do not imply automatic lookup or metric evaluation. Keep the native document when a workflow needs source-only metadata.

## ODCS

The native model includes context blocks, schema/server variants, nested logical types and temporal options, relationships, teams, roles, pricing, support channels, custom properties, SLA metadata, and textual/library/SQL/custom quality rules with schedules. Deserializing or preserving these definitions does not execute quality rules or schedules. Current canonical mapping does not populate native ODCS quality rules; do not present their presence in YAML as runtime validation.

SLA and lifecycle metadata **are** normalized into canonical commitments. Unknown/invalid SLA properties remain in extensions with diagnostics. See [commitments and temporal values]({{ '/concepts/commitments/' | relative_url }}).

## Export boundaries

JSON Schema export represents one record using Draft 2020-12. Required properties and nullability are independent. Unsupported logical types, invalid constraints and constraints requiring dataset-wide evaluation fail conversion; physical storage, governance and quality execution are outside that representation. A CSV dialect export describes endpoint parsing settings and does not scan the CSV. See [contract tasks]({{ '/guides/contracts/' | relative_url }}) and [dialect export]({{ '/csv-dialect-export/' | relative_url }}).
