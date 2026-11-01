---
title: Guides
nav_order: 3
nav_exclude: false
has_children: true
permalink: /guides/
---

Choose a guide for the task you want to complete. Each explains when to use the
operation, how to run it, and what to expect from the result.

- [Validate, inspect, diff and export contracts]({{ '/guides/contracts/' | relative_url }}): check a descriptor, explore its contents, review changes, or export a record schema.
- [Read data and infer schemas]({{ '/guides/data/' | relative_url }}): preview records and use the data to suggest field definitions.
- [Serve asset data over HTTP]({{ '/http-serving/' | relative_url }}): embed a read-only host or use `data serve` with negotiated streaming formats.
- [Configure storage and readers]({{ '/guides/readers-storage/' | relative_url }}): choose a supported source and add the readers your .NET application needs.
- [Export a CSV dialect]({{ '/csv-dialect-export/' | relative_url }}): share a CSV endpoint's parsing settings with another tool.
- [Connect to databases]({{ '/database-integration/' | relative_url }}): configure connectors and runtime credentials for preview, inference and .NET applications.
- [Plan and provision a database]({{ '/guides/provisioning/' | relative_url }}): review generated SQL, deploy a schema and load data through the .NET library.

CLI and .NET examples are identified separately. Provisioning is currently available
through .NET; its CLI command is planned.

Use the [reference]({{ '/reference/' | relative_url }}) for every command option
and API entry point, or read the [concepts]({{ '/concepts/' | relative_url }})
for the shared contract model and compatibility rules.
