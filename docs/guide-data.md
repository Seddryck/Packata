---
title: Read data and infer schemas
parent: Guides
nav_order: 2
nav_exclude: false
permalink: /guides/data/
---

Select a canonical asset and its endpoint binding explicitly. Preview reads values; inference discovers structure. Neither task is equivalent to validating all data against a contract.

## CLI

Run from the [sample]({{ '/quickstart/cli/' | relative_url }}) directory:

```console
packata data preview datapackage.json --asset customers --columns name,id --limit 2 --output csv
packata schema infer datapackage.json --asset customers --header present --maximum-records 100
packata schema infer datapackage.json --asset customers --output enriched.json
```

The first result starts with `name,id`. Inference prints its decisions and leaves the source unchanged unless `--write` is explicitly selected. `--output` creates a new enriched descriptor; it is not a representation selector here. Existing output files are protected. Review warnings about lost comments/formatting or unsupported source metadata before replacing a contract. Schema inference also accepts bound database assets and uses database metadata rather than CSV sampling settings.

See the [complete inference options]({{ '/cli-inference/' | relative_url }}), [preview reference]({{ '/cli/' | relative_url }}#preview-bound-data), and [database configuration]({{ '/cli-database/' | relative_url }}).

## .NET

Follow the [.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}) to read the same sample through `IDataReader`. For CSV inference, the library can start from a stream without a pre-existing bound descriptor:

```csharp
using Packata.ResourceReaders;

using var stream = File.OpenRead("customers.csv");
var result = new CsvDataContractInference().Infer(stream, "customers.csv");
foreach (var diagnostic in result.Diagnostics)
    Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
var inferred = result.RequireContract();
```

`CsvDataContractInferenceOptions` controls sampling/culture/header/dialect and generated identity/asset/endpoint metadata. Database inference uses `DatabaseDataContractInference` with `DatabaseDataContractInferenceOptions`; discovery can return partial metadata with diagnostics. The caller configures database connectivity. Inference produces a canonical result; source-document updating is a separate CLI responsibility.

## Limits

Preview is bounded by a positive row limit, but providers can buffer internally. Only registered formats are available. Inference currently supports CSV-family file bindings and database bindings; it does not infer every format the reader can open. See [reader/storage configuration]({{ '/guides/readers-storage/' | relative_url }}).
