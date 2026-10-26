---
title: CLI Quickstart
parent: Getting started
nav_order: 1
nav_exclude: false
permalink: /quickstart/cli/
---

This walkthrough checks a small customer dataset's contract, reads two records,
and exports its schema and CSV parsing settings. You can run every step in a
terminal without writing application code.

You need a .NET 8, 9 or 10 SDK to install the CLI with the commands below. If you
prefer an executable that includes .NET, follow the
[standalone installation instructions]({{ '/installation/' | relative_url }}#standalone-archives)
and then continue at the sample download step.

## Install the CLI

```console
dotnet tool install --global Packata-cli
packata --version
```

The first command installs `packata` as a global .NET tool. The second prints the
installed version, confirming that your terminal can find it. If it is already
installed, use `dotnet tool update --global Packata-cli` instead. If the command
cannot be found, follow the installer's PATH instructions for your shell.

## Download the sample

Save [datapackage.json]({{ '/samples/quickstart/datapackage.json' | relative_url }})
and [customers.csv]({{ '/samples/quickstart/customers.csv' | relative_url }}) in the
same new directory, then open your terminal there. In a repository checkout,
the originals are in `docs/samples/quickstart`.

`datapackage.json` is the contract: it describes a dataset named `customers`, with
an integer `id` and a string `name`, and points to `customers.csv`. The CSV holds
the actual records for Alice and Bob. Keep both files together so the relative
CSV path can be resolved.

## Check the contract

Run validation before reading the data:

```console
packata contract validate datapackage.json
```

For this sample, the command prints `Contract is valid.` and exits with code `0`.
It checks the descriptor against its standard. It does not open the CSV or prove
that every record matches the schema.

## See what the contract describes

Inspect the customer dataset:

```console
packata contract inspect datapackage.json --asset customers
```

`--asset customers` selects the dataset by its identifier. The result shows its
`id` and `name` fields and the endpoint named `customers-endpoint`, which identifies
where its data is stored. This step still reads only the contract.

## Read two records

Preview the data referenced by that asset:

```console
packata data preview datapackage.json --asset customers --limit 2 --output-format json
```

`--limit 2` reads at most two records. `--output-format json` makes the result easy
to inspect or pass to another program. You should see:

```json
{"schemaVersion":1,"columns":["id","name"],"rows":[[1,"Alice"],[2,"Bob"]]}
```

Whitespace can differ. Unlike validation and inspection, preview opens the CSV.
None of these commands changes either sample file.

If you receive the descriptor and CSV together in `dataset.zip`, you can also run
`packata data preview dataset.zip --asset customers --limit 2 --output-format json`
without extracting it. See the [zipped package example]({{ '/guides/data/' | relative_url }}#preview-a-zipped-data-package)
for the archive layout and resource paths.

## Export a record schema

To give another tool a JSON Schema for customer records, run:

```console
packata schema export datapackage.json --output-format json-schema --asset customers
```

The generated schema describes one record with an integer `id` and a string
`name`. Export prints the schema; it does not validate the CSV against it. The
[contract guide]({{ '/guides/contracts/' | relative_url }}#export-a-record-schema)
shows how to save it to a file.

## Export the CSV parsing settings

A tool reading the CSV also needs to know its delimiter and header settings.
Export those from the endpoint you found during inspection:

```console
packata dialect export datapackage.json --endpoint customers-endpoint
```

The resulting Table Dialect JSON descriptor specifies a comma delimiter, a header
and the sample's newline setting. It exports settings already in the contract;
it does not infer them by sampling the CSV. The
[dialect guide]({{ '/csv-dialect-export/' | relative_url }}) explains how to save
and interpret the result.

## Continue with your own task

Use the [data guide]({{ '/guides/data/' | relative_url }}) to infer a schema from
records, or the [contract guide]({{ '/guides/contracts/' | relative_url }}) to
review a diff and assess compatibility between versions. The
[CLI reference]({{ '/cli/' | relative_url }}) lists all commands and options.

If you want to embed these operations in an application, follow the separate
[.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}).
