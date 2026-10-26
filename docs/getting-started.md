---
title: Getting started
nav_order: 2
nav_exclude: false
has_children: true
permalink: /getting-started/
---

Start with the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) to try
Packata without writing application code. You will download a small customer
dataset, check its contract and read its first records. Each step shows the
result you should expect.

If you are still deciding whether Packata fits your work,
[Why Packata?]({{ '/docs/why-packata/' | relative_url }}) explains its purpose and
the tasks it supports.

## Choose how you want to use Packata

| You want to… | Start here | What you will do |
| --- | --- | --- |
| Work in a terminal | [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}) | Validate and inspect a contract, preview records, and export metadata |
| Add contract-based data access to a .NET application | [.NET Library Quickstart]({{ '/quickstart/dotnet/' | relative_url }}) | Load the same sample contract and read its CSV from C# |
| Work inside VS Code | [VS Code status]({{ '/quickstart/vscode/' | relative_url }}) | Review the proposed extension; it is **planned, not implemented** |

The CLI and .NET library share the same contract model. Choose the terminal for
supported commands or the library when your application needs to call the APIs
directly. You do not need to complete both Quickstarts.

## Get the tools you need

Each Quickstart includes the installation steps for its example. The
[installation page]({{ '/installation/' | relative_url }}) covers the alternatives:
a global or repository-local CLI tool, standalone executables, and optional .NET
packages for additional readers, storage and database operations.

After your first working example, use the [guides]({{ '/guides/' | relative_url }})
to explore a task in more detail. The [CLI reference]({{ '/cli/' | relative_url }})
lists every command and option, while [Concepts]({{ '/concepts/' | relative_url }})
explains the terms used in contracts.
