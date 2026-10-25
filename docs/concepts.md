---
title: Concepts
nav_order: 4
nav_exclude: false
has_children: true
permalink: /concepts/
---

Packata separates a contract's source standard from the interfaces used to work with it.

| Concept | Meaning |
| --- | --- |
| Native document | The Data Package or ODCS object model, retaining standard-specific fields |
| Canonical contract | Shared identity, metadata, governance, assets, endpoints, commitments and extensions |
| Asset | A dataset with an identifier, schema and endpoint bindings |
| Schema / field | Logical structure, nested fields, keys, constraints, physical names and types |
| Endpoint | File, inline or database location plus format and connection metadata |
| Binding | Connects an asset to an endpoint; may select a physical table/path |

Mapping a document produces a value and diagnostics. Read warnings before using the result; ambiguous bindings or unsupported metadata cannot be silently treated as a complete mapping. Information preserved in namespaced `datapackage` or `odcs` extensions is not necessarily executable behavior. Native deserialization, canonical mapping, reading data, and evaluating a declared rule are separate capabilities.

Use the native model for source-specific editing. Use the canonical model to share readers, provisioners, inference and comparison between standards.

## Factual differences and validation

Document validation checks a descriptor; it does not validate all rows in its datasets. A factual diff reports additions, removals and modifications from a previous baseline to a current candidate; it does not decide compatibility or whether a change breaks consumers. Changing an identifier is a removal plus an addition, not inferred renaming.

Differences use ordinal semantic identifiers and escaped paths, preserve previous/current values and immutable snapshots, and sort by path and concrete change kind. Duplicate identities and unsupported snapshot values fail explicitly. See the [.NET comparison reference]({{ '/reference/dotnet/' | relative_url }}#comparison) and [CLI diff]({{ '/cli/' | relative_url }}#compare-two-contract-versions).

## Capability boundaries

Consumer compatibility analysis is implemented by `contract compare` and `ContractCompatibility.Compare`; see [the directional policy]({{ '/compatibility/' | relative_url }}). Factual `contract diff` remains separate. Dataset validation, MCP exposure, a GitHub Action integration, and command-line provisioning remain separate roadmap work. The VS Code extension is **planned — not implemented**; see its [status]({{ '/reference/vscode/' | relative_url }}).

## Value domains

[Shared value domains]({{ '/value-domains/' | relative_url }}) distinguish descriptive entries/labels from the effective allowed-value set. Category-table references preserve unknown membership without fetching rows; see [Data Package categories]({{ '/datapackage-categories/' | relative_url }}).
