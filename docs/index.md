---
layout: page
title: Home
nav_order: 1
nav_exclude: false
permalink: /
---

# Packata

Transform data. Transform possibilities.
{: .fs-6 .fw-300 }

Packata is a standards-neutral .NET library for working with Data Package v2 and Open Data Contract Standard (ODCS) documents. Map both formats to a shared model for data assets, schemas, metadata, and endpoints, then read or provision data with the optional packages.

[Get started]({{ '/docs/why-packata/' | relative_url }}){: .btn .btn-primary }
[Command-line interface]({{ '/cli/' | relative_url }}){: .btn }

## Explore the documentation

- [Why Packata?]({{ '/docs/why-packata/' | relative_url }})
- [Database connections, connectors and provisioning]({{ '/database-integration/' | relative_url }})
- [Export a CSV dialect descriptor]({{ '/csv-dialect-export/' | relative_url }})
- [Command-line interface]({{ '/cli/' | relative_url }})
- [Database commands and configuration]({{ '/cli-database/' | relative_url }})

## Install

Packata targets .NET 8, .NET 9, and .NET 10. Install the package for your document format:

```console
dotnet add package Packata.DataPackage
dotnet add package Packata.OpenDataContract
```

Add `Packata.ResourceReaders`, `Packata.Storages`, or `Packata.Provisioners` when your application needs those capabilities.

## Get involved

[Ask a question](https://github.com/Seddryck/Packata/discussions), [report an issue](https://github.com/Seddryck/Packata/issues), or [contact the project]({{ '/contact/' | relative_url }}).
