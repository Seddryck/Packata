---
title: File schema inference
parent: Reference
nav_exclude: true
permalink: /file-schema-inference/
---

# File schema inference

Parquet inference is provided by `Packata.ResourceReaders.Parquet`:

```csharp
using Packata.ResourceReaders.Parquet;

var result = await new ParquetDataContractInference().InferAsync("measurements.parquet");
if (result.IsSuccessful)
    Console.WriteLine(result.Contract.Assets[0].Schema!.Fields.Count);
foreach (var diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.SourcePath}: {diagnostic.Message}");
```

The options support contract identity/metadata, asset id/name/physical name/description,
and endpoint id/name overrides. No reader-provider registration is needed for inference.
Register `AddParquet()` when subsequently reading rows through `ResourceReaderFactory`.

Parquet inference reads the footer schema, never column data or row groups. It works
with empty files and preserves declared nullability, names, order, physical types,
decimal precision/scale, timestamp UTC adjustment and time unit. Primitive CLR types
map to canonical scalar types; structs map to object children and lists to array items.
Binary columns use string/binary. Maps, legacy repeated fields and unsupported scalar
types return error diagnostics with field paths; do not persist an unsuccessful result.
No keys, relationships, ownership or quality rules are invented.

Caller streams must be readable and seekable. They remain open and their position is
restored even on failure. Non-seekable input is rejected, never buffered automatically.
Cancellation is passed to metadata I/O. Defaults limit serialized footers to 16 MiB,
schema fields to 10,000 and nesting to 32 levels. These are not a bound on every
allocation made by the underlying metadata decoder. Encrypted footers are unsupported.

The CLI resolves a single file from an existing asset binding, with explicit format
taking precedence over recognized media type and path extension (`parquet`/`pqt`):

```console
packata schema infer contract.yaml --asset measurements
packata schema infer contract.yaml --asset measurements --output enriched.yaml
packata schema infer contract.yaml --asset measurements --write
```

`packata infer` remains an alias. Display is read-only; output/write are mutually
exclusive and persistence uses the existing atomic writer. CSV sampling, culture,
header and dialect switches are rejected when explicitly supplied for Parquet.
Multi-file bindings and externally compressed Parquet files are rejected.

ODCS and Data Package updates preserve unrelated document content and authored
semantic metadata. File updates do not create CSV dialects for Parquet. The current
document updater persists scalar logical types/nullability; physical Parquet facets
remain available in the library result and produce a persistence warning. Nested
schemas can be inferred in the library but document persistence fails explicitly
before writing, because the updater cannot yet serialize them faithfully.
