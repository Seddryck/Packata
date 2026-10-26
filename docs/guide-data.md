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
packata data preview datapackage.json --asset customers --columns name,id --limit 2 --output-format csv
packata schema infer datapackage.json --asset customers --header present --maximum-records 100
packata schema infer datapackage.json --asset customers --output enriched.json
```

The preview example writes the columns in the requested order, so its first line is `name,id`.
The inference examples report the structure found in the data. They leave the source descriptor
unchanged unless you explicitly choose `--write`.

Use `--output` to save an enriched descriptor to a new file. Existing files are protected.
Before replacing a contract, review warnings about comments, formatting or source metadata
that the update cannot preserve.

For an asset bound to a database, inference reads database metadata instead of sampling CSV
records. CSV sampling settings do not change that discovery process.

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

Configure CSV sampling, culture, header detection and dialect inference through
`CsvDataContractInferenceOptions`. The same options object lets you override the generated
contract identity and asset or endpoint metadata.

For databases, use `DatabaseDataContractInference` with `DatabaseDataContractInferenceOptions`
and configure connectivity in your application. Discovery can return partial metadata, so
review its diagnostics before accepting the result. Library inference returns a canonical
contract; updating the original source document is a separate CLI operation.

## Limits

Preview is bounded by a positive row limit, but providers can buffer internally. Only registered formats are available. Inference currently supports CSV-family file bindings and database bindings; it does not infer every format the reader can open. See [reader/storage configuration]({{ '/guides/readers-storage/' | relative_url }}).
