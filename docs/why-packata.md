---
title: Why Packata?
nav_order: 0
permalink: /docs/why-packata/
parent: Getting started
nav_exclude: false
---

Teams can describe their datasets using different contract standards. One project
may use Data Package, while another uses ODCS. Both still need to inspect fields,
read records and review changes. Implementing those operations separately for
each standard repeats much of the same work.

Packata provides common commands and .NET APIs for those tasks. It reads the
source document and maps it to a shared contract model, which describes the
datasets, their fields and the locations where their data can be read.

## Use the same operations across standards

For example, `contract inspect` shows the datasets and fields in either a
Data Package or ODCS document. `data preview` reads records from the selected
dataset when its format and storage are supported. Schema inference can suggest
field definitions from data, while diff and compatibility analysis help you
review a proposed contract change.

Use the CLI when those commands fit your workflow. Use the .NET libraries when
you want to call the operations directly from application code or configure
additional readers and storage backends. Database provisioning is also available
through .NET; its CLI command is planned.

## Keep the source standard's meaning

The shared model gives these operations a common view of a contract, but each
standard still has its own structure and declarations. Use the native document
model when editing standard-specific fields, and review mapping diagnostics to
see what could not be represented fully.

A rule can be preserved as metadata without being checked against dataset rows.
The [standards and limitations]({{ '/standards/' | relative_url }}) page explains
those boundaries, so you can judge whether Packata supports your intended task.

## Try a working example

Start with the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) to check a
contract and preview two customer records. Choose the
[.NET Library Quickstart]({{ '/quickstart/dotnet/' | relative_url }}) if you want
to build an application that reads the same sample from C#.

The [concepts overview]({{ '/concepts/' | relative_url }}) explains the asset,
endpoint and binding terminology you will encounter in those examples.
