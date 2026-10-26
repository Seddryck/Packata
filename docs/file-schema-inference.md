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
restored even on failure. The library rejects non-seekable input. The CLI provides
bounded temporary-file staging for HTTP/archive inputs before calling these APIs.
Preview uses the same adapter. `--staging-maximum-bytes` defaults to 256 MiB and is
independent of Parquet's footer metadata limit. Seekable inputs pass through without
copying. See [shared staging limits and ownership]({{ '/guides/readers-storage/' | relative_url }}#stage-excel-and-parquet-resources)
and [CLI inference]({{ '/cli-inference/' | relative_url }}) for workbook/sample limits.
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

## Excel worksheets

Install `Packata.ResourceReaders.Excel` for `.xls`/`.xlsx` inference:

```csharp
using Packata.ResourceReaders.Excel;

var inference = new ExcelDataContractInference(new()
{
    SheetName = "Measurements",
    Header = ExcelHeaderMode.Present,
    MaximumRecords = 1000
});
var result = inference.Infer("measurements.xlsx");
```

The same contract/asset/endpoint identity overrides as Parquet are available.
Inference needs no provider registration; register `AddExcel()` for row reading.
Pass the inferred schema when reading to retain generated names and trimmed width.

```console
packata schema infer contract.yaml --asset measurements --sheet-name Measurements --header present
packata schema infer contract.yaml --asset measurements --sheet-number 2 --maximum-records 500 --write
```

Sheet name/number are mutually exclusive, use one-based numbers and override endpoint
hints. Otherwise endpoint selection applies, defaulting to the first worksheet.
Explicit header present/absent overrides endpoint hints; auto uses a hint when supplied.
Without hints, distinct text labels followed by typed cells suggest a header. Other
cases retain the first row as data with an ambiguity warning. No arbitrary confidence
score is attached. Generated blank/duplicate names are stable (`column1`, `name_2`).

Profiling assumes one rectangular table in one worksheet. Leading blank rows are
skipped (up to 1,000 by default); internal blank rows remain observations. Trailing
empty columns are trimmed, including formatted cells without values; entirely blank
internal columns remain unknown. Empty and header-only sheets return warnings and
unknown types where names are available. Sampling includes at most MaximumRecords
data rows, plus up to two lookahead/header rows, and reports truncation. Rows beyond
the limit cannot establish types or additional data columns.

Strings keep their original semantics, including numeric-looking identifiers. Native
cell booleans, numbers, DateTime and TimeSpan map to canonical types. Numeric int/double
mixes promote to number; other mixed columns fall back to string with warnings, without
converting workbook cells. No decimal precision, displayed zero mask, enum, primary key
or timezone is inferred. Date recognition and workbook 1900/1904 handling are delegated
to ExcelDataReader. Numbers with formatting remain numbers unless the decoder returns
a temporal value; diagnostics explain that display formats are not contractual types.

All newly inferred fields are optional: no blanks in a sample proves requiredness.
Library field extensions record observed blanks and sample row counts. CLI merging
preserves authored requiredness, and keeps an existing type when the sample has no
type evidence. Updates are refused if authored fields were not observed, including
an empty worksheet, to avoid deleting their metadata. Formula results use stored caches: formulas are not evaluated, links
are not refreshed, and cache freshness or formula/error identity is not established.
Diagnostics always disclose those decoder limitations. Merged cells produce warnings
and are never filled forward.

Defaults limit workbook bytes to 64 MiB, sheet width to 1,024 columns (including
formatted empty columns), and sampled data rows to 1,000. The byte limit applies to
the complete input, unlike CSV's sampled-byte limit; it does not bound ZIP expansion
or all allocations inside the workbook decoder. Cancellation is checked between
rows; synchronous workbook opening itself is not interruptible. Streams must be
readable/seekable and remain open with their position restored. Invalid, encrypted,
password-protected or unsupported workbooks fail without updating the contract.

Named tables, arbitrary ranges, multiple tables, multi-row headers, and noncontiguous
comment rows are unsupported and configured selections fail explicitly. Leading
comment rows and a single header row are supported. Selected sheet/header/leading-row
decisions persist as spreadsheet dialect options in Data Package and Packata custom
properties in ODCS; reloading and preview use those same decisions. Existing numeric
sheet selection is replaced by the resolved name to prevent conflicting hints.
CSV culture and dialect-inference switches are rejected for Excel. The same display,
output, atomic write and compatibility-alias behavior applies to all inference paths.
