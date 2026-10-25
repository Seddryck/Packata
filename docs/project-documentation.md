---
title: Documentation maintenance and coverage
parent: Project
nav_order: 6
nav_exclude: false
permalink: /project/documentation/
---

The site separates first-use Quickstarts, task guides, interface reference, shared concepts/standards, and project information. Keep task explanations shared and label interface-specific examples. The CLI is the default Quickstart; VS Code is **planned — not implemented**, with status pages only.

## Existing page migration map

All 13 original content/utility URLs remain available; no redirect plugin is required.

| Previous source / URL | Destination |
| --- | --- |
| `index.md` / `/` | Home with CLI Get started action and interface chooser |
| `_docs/why-packata.md` / `/docs/why-packata/` | `why-packata.md`, Concepts; same URL |
| `cli.md` / `/cli/` | Reference / Command-line interface; retained detail and expanded options |
| `cli-database.md` / `/cli-database/` | CLI child reference for every connector/config command |
| `database-integration.md` / `/database-integration/` | Guides / database connection workflow with interface-specific sections |
| `csv-dialect-export.md` / `/csv-dialect-export/` | Guides / shared dialect workflow with CLI and .NET sections |
| `brand-colors.md` / `/brand-colors/` | Project / Brand colors; swatches, usage, preview and accessibility retained |
| `contact.md` / `/contact/` | Project / support and contribution routes |
| `changelog-timeline.md` / `/changelog-timeline/` | Project / news and history; existing data/include preserved |
| `changelog-posts.md` / `/changelog-posts/` | Secondary changelog view linked from Project; layout preserved |
| `blog/index.html` / `/blog/` | Compatibility news landing page linking to timeline/releases |
| `thanks.md` / `/thanks/` | Hidden legacy form-confirmation route; placeholder copy replaced; no active Formspree form remains |
| `404.md` / `/404.html` | Hidden utility page with recovery links |

README now points to the distinct Quickstarts and full CLI reference. Root `MIGRATION.md` remains available to repository users; keep its package/API guidance aligned with the website migration page.

## Capability destinations

This inventory covers user-visible feature groups through `origin/main` commit `5d3802f`, including features without old standalone pages. The MVP provides concise coverage and public API links; detailed tutorials and generated API documentation can follow.

| Feature group | Documentation destination |
| --- | --- |
| Tool/archive distribution, TFMs, RIDs/libc | [Installation]({{ '/installation/' | relative_url }}) |
| All CLI arguments, inherited options, aliases, output and exit codes | [CLI reference]({{ '/cli/' | relative_url }}), [inference]({{ '/cli-inference/' | relative_url }}), [database/config]({{ '/cli-database/' | relative_url }}) |
| CSV/database schema inference | [Data tasks]({{ '/guides/data/' | relative_url }}) and [.NET entry points]({{ '/reference/dotnet/' | relative_url }}) |
| Reader providers, formats/dialects, parsing and storage | [Readers/storage guide]({{ '/guides/readers-storage/' | relative_url }}) |
| Data Package descriptors, inline data, constraints/categories/metrics | [Contracts and standards]({{ '/standards/' | relative_url }}) |
| ODCS model, schema/server variants, governance and quality definitions | [Contracts and standards]({{ '/standards/' | relative_url }}) |
| Canonical mapping, diagnostics/extensions and removed APIs | [Concepts]({{ '/concepts/' | relative_url }}) and [migration]({{ '/migration/' | relative_url }}) |
| SLA/lifecycle normalization and offset-bearing temporal values | [Commitments and temporal values]({{ '/concepts/commitments/' | relative_url }}) |
| Typed factual differences and immutable snapshots | [Contract tasks]({{ '/guides/contracts/' | relative_url }}) and [.NET comparison]({{ '/reference/dotnet/' | relative_url }}#comparison) |
| JSON Schema and CSV dialect metadata exports | [Contract tasks]({{ '/guides/contracts/' | relative_url }}), [dialect guide]({{ '/csv-dialect-export/' | relative_url }}) and CLI reference |
| Shared database service, connector acquisition/config and runnable examples | [Database integration]({{ '/database-integration/' | relative_url }}) and [.NET examples]({{ '/reference/dotnet/' | relative_url }}#public-source-and-runnable-examples) |
| Schema plans, physical metadata, constraints, preflight and bulk loading | [Provisioning guide]({{ '/guides/provisioning/' | relative_url }}) |
| Brand, support, history, utility pages | [Project]({{ '/project/' | relative_url }}) and migration map above |

Dataset validation, compatibility policy/analysis, MCP exposure, GitHub Action integration, and CLI provisioning are separate roadmap work. Native quality-rule parsing is not rule execution. Removed Didot templates and retired packages are historical material, not pages to restore.

## Validation and authoring

See [docs/README.md](https://github.com/Seddryck/Packata/blob/main/docs/README.md) for Docker/Ruby preview and strict production build commands. Navigation uses `parent`, optional `grand_parent`, `has_children`, and `nav_order` on ordinary Jekyll pages; all public permalinks are explicit. Use `relative_url` for site-local links to preserve `/Packata`.

`Validate-Documentation.mjs` discovers command registrations and inherited settings from source and checks the corresponding marked reference sections, including positional arguments and short aliases. The optional runtime check compares all command help pages. After building, it checks rendered internal links, fragments and sample downloads. Maintain the `cli:` markers when reorganizing reference sections. It checks completeness, not the semantic correctness of prose; review defaults, constraints and side effects against implementation/tests as well.

`Test-DocumentationQuickstarts.mjs` copies the sample to a fresh isolated directory, exercises CLI outputs and source preservation, then compiles and executes the actual C# Quickstart block against the upcoming source version. It does not install global tools or alter user configuration. Published package installation is documented separately; these checks do not assert an older released package already includes unreleased APIs.
