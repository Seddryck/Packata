---
title: Why Packata?
nav_order: 0
permalink: /docs/why-packata/
parent: Getting started
nav_exclude: false
---

Working with a data contract should not mean rebuilding the same tools for every
standard. A customer dataset described in Data Package and one described in ODCS
may both need to be inspected, read, compared with an earlier version, or used to
create database tables.

Packata reads each standard into its own document model, then maps it to a shared
contract model. Readers and other operations can work with that shared model
instead of implementing each standard's document structure again. You can use
the CLI to inspect contracts, read data, infer schemas, review differences and
assess consumer compatibility, or embed the .NET libraries in an application.

For example, the same `contract inspect` command can show the datasets and fields
in a Data Package or ODCS document. `data preview` can then read a selected asset
through its binding, provided its format and storage are supported. In .NET,
provisioners can generate a database schema from the mapped contract. Database
provisioning is available through .NET; its CLI command is planned.

The source standard still matters. Use its native document model for edits to
standard-specific fields, and review mapping diagnostics before relying on the
shared representation. A declaration preserved as metadata is not necessarily
an executable rule. The [standards and limitations]({{ '/standards/' | relative_url }})
page explains what is supported.

Start with the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) or the
[.NET Library Quickstart]({{ '/quickstart/dotnet/' | relative_url }}). The
[concepts overview]({{ '/concepts/' | relative_url }}) explains assets, endpoints
and the other terms used throughout the documentation.
