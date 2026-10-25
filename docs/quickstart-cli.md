---
title: CLI Quickstart
parent: Getting started
nav_order: 1
nav_exclude: false
permalink: /quickstart/cli/
---

Get a useful result with a local CSV and Data Package contract. You need a supported .NET SDK/runtime for the tool (.NET 8, 9 or 10) and a terminal. [Standalone archives]({{ '/installation/' | relative_url }}#standalone-archives) work without installing .NET separately.

## Install and get the sample

```console
dotnet tool install --global Packata-cli
packata --version
```

Download [datapackage.json]({{ '/samples/quickstart/datapackage.json' | relative_url }}) and [customers.csv]({{ '/samples/quickstart/customers.csv' | relative_url }}) into the same new directory. Open your terminal in that directory. The checked-in originals are in `docs/samples/quickstart` in the repository.

## Validate, inspect and read

```console
packata contract validate datapackage.json
packata contract inspect datapackage.json --asset customers
packata data preview datapackage.json --asset customers --limit 2 --output json
```

Validation prints `Contract is valid.` and exits with `0`. Inspection shows the `customers` asset and `customers-endpoint` endpoint. Preview returns:

```json
{"schemaVersion":1,"columns":["id","name"],"rows":[[1,"Alice"],[2,"Bob"]]}
```

Whitespace can differ. Validation and inspection read metadata; preview reads the CSV. Neither changes the sample files.

## Export and continue

```console
packata export datapackage.json --format json-schema --dataset customers
packata dialect export datapackage.json --endpoint customers-endpoint
```

The first command returns a record schema with integer `id` and string `name`; the second returns a Table Dialect JSON descriptor. Next, [infer a schema]({{ '/guides/data/' | relative_url }}), [compare contracts]({{ '/guides/contracts/' | relative_url }}), or browse the [complete CLI reference]({{ '/cli/' | relative_url }}). For embedding Packata, use the separate [.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}).
