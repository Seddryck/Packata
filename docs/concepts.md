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

Mapping returns a value together with diagnostics. Read the warnings before using the result:
ambiguous bindings or unsupported metadata can mean that the mapping is incomplete.

The model can preserve information in `datapackage` or `odcs` extensions without providing
behavior that uses it. Reading a native document, mapping it, reading data and evaluating a
declared rule are separate capabilities.

Use the native model for source-specific editing. Use the canonical model to share readers, provisioners, inference and comparison between standards.

## Factual differences and validation

Document validation checks a descriptor; it does not validate all rows in its datasets. A factual diff reports additions, removals and modifications from a previous baseline to a current candidate; it does not decide compatibility or whether a change breaks consumers. Changing an identifier is a removal plus an addition, not inferred renaming.

Differences match by ordinal semantic identifiers and use escaped paths. They retain previous
and current values in immutable snapshots, ordered by path and then concrete change kind.
Duplicate identities and unsupported snapshot values fail explicitly.

The [.NET reference]({{ '/reference/dotnet/' | relative_url }}#comparison) explains the API,
and the [CLI diff reference]({{ '/cli/' | relative_url }}#compare-two-contract-versions)
describes command output.

## Capability boundaries

Use `contract compare` or `ContractCompatibility.Compare` for consumer compatibility
analysis. The [directional policy]({{ '/compatibility/' | relative_url }}) explains its
assumptions. Use `contract diff` for the separate factual comparison.

Dataset validation, MCP exposure, GitHub Action integration and command-line provisioning
remain separate roadmap work. The VS Code extension is **planned — not implemented**;
its [status page]({{ '/reference/vscode/' | relative_url }}) tracks that distinction.

## Value domains

[Shared value domains]({{ '/value-domains/' | relative_url }}) distinguish descriptive entries/labels from the effective allowed-value set. Category-table references preserve unknown membership without fetching rows; see [Data Package categories]({{ '/datapackage-categories/' | relative_url }}).
