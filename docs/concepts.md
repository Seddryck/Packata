---
title: Concepts
nav_order: 4
nav_exclude: false
has_children: true
permalink: /concepts/
---

A data contract describes a dataset: what its fields mean, which values are
allowed, where to find the data, and any promises about its delivery. Packata
lets you work with contracts written in Data Package or ODCS through the same
commands and shared .NET model.

Start with a customer dataset stored in `customers.csv`. The contract might say
that it has an integer `id`, a string `name`, and a CSV header. The contract is the
description; the CSV contains the actual records. Loading or validating the
description does not read those records.

## Connect the description to the data

Packata separates the dataset's meaning from its storage location. This lets the
same dataset be described independently of a particular file or database table.

| Term | Meaning in the customer example |
| --- | --- |
| Asset | The `customers` dataset, identified by its asset ID |
| Schema | The field definitions, such as integer `id` and string `name`, with their keys and constraints |
| Endpoint | The location and access settings, such as a CSV file with a delimiter or a database connection |
| Binding | The link from `customers` to an endpoint, selecting a path or physical table where needed |

An asset can have more than one binding. For example, the customer dataset might
be available as a CSV and as a database table. An application must choose which
representation to read. The [storage and reader guide]({{ '/guides/readers-storage/' | relative_url }})
explains how those locations are opened.

## Work with different contract standards

Data Package and ODCS organize their documents differently. Packata first reads
a document into the model for its source standard, called the **native model**.
This retains the standard-specific fields and is the right place for edits that
need to preserve that standard's structure.

Packata can then map that document to its **canonical model**, the shared
representation used by readers, inference, comparison and provisioners. It holds
contract identity, descriptive metadata, governance, assets, endpoints, service
commitments and extensions. Code using this model does not need a separate reader
workflow for each source standard.

Mapping returns diagnostics as well as a value. Read them: an ambiguous binding
or unsupported declaration can leave part of the shared representation incomplete.
Packata may retain information in `datapackage` or `odcs` extensions even when it
has no behavior that acts on that information. Preserving a rule does not mean
Packata has checked it against the data.

## Factual differences and validation

Validation, reading records, diffing and compatibility analysis answer different
questions:

| Operation | Question it answers |
| --- | --- |
| Validate a contract | Does the descriptor follow its source standard? |
| Preview data | Can Packata read a few records through this binding? |
| Diff two contracts | What was added, removed or modified? |
| Assess compatibility | Could the proposed contract break consumers of the previous one? |

For example, removing `name` from the schema is a fact reported by a diff.
Compatibility analysis additionally explains that consumers may still expect
that field. A valid descriptor alone establishes neither data quality nor safe
compatibility.

Diff matches datasets and fields by their case-sensitive identifiers. Renaming
one is reported as a removal and an addition; Packata does not infer a rename.
Change paths escape special characters so identifiers remain distinguishable.
Previous and current values are captured as immutable snapshots, and results
are sorted by path and then change kind. Duplicate identifiers or values that
cannot be snapshotted cause an explicit failure rather than an empty report.

The [contract guide]({{ '/guides/contracts/' | relative_url }}) walks through these
operations. The [.NET comparison reference]({{ '/reference/dotnet/' | relative_url }}#comparison)
and [CLI diff reference]({{ '/cli/' | relative_url }}#compare-two-contract-versions)
provide the API and output details.

## Explore the other concepts

- [Service commitments and dates]({{ '/concepts/commitments/' | relative_url }}): understand delivery promises, lifecycle milestones and the difference between local times and instants.
- [Value domains]({{ '/value-domains/' | relative_url }}): distinguish a catalog of named categories from the values a field actually allows.
- [Contract compatibility]({{ '/compatibility/' | relative_url }}): understand how a change affects consumers, and why comparison has a direction.

[Data Package categories]({{ '/datapackage-categories/' | relative_url }}) gives
the standard-specific syntax for inline categories and references to category tables.

## Capability boundaries

Consumer compatibility analysis is implemented through `contract compare` and
`ContractCompatibility.Compare`. It assesses declared guarantees using the
[documented policy]({{ '/compatibility/' | relative_url }}); it does not test real
consumer applications.

Dataset-wide validation, MCP exposure, GitHub Action integration and command-line
provisioning remain separate roadmap work. Database provisioning is available in
.NET. The VS Code extension is **planned, not implemented**; its
[status page]({{ '/reference/vscode/' | relative_url }}) describes that boundary.
