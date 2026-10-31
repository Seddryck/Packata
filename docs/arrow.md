---
title: Arrow record batches
parent: Reference
nav_order: 8
permalink: /reference/dotnet/arrow/
---

# Convert a Packata reader into Arrow batches

Install the optional `Packata.Arrow` package alongside your reader packages. It references
`Packata.Core` and Apache Arrow; Core has no Arrow dependency. The adapter produces schemas
and record batches independently of IPC, Flight, Python bindings, or any transport.

The following example uses the existing `ResourceReaderFactory` with an inline endpoint.
For a contract-defined asset, supply `asset.Schema` and the reader opened for that asset.

```csharp
using Apache.Arrow;
using Packata.Arrow;
using Packata.Core.Contracts;
using Packata.ResourceReaders;

var schema = new DataSchema([
    new DataField("id", "integer"),
    new DataField("name", "string", Nullable: true)
]);
var endpoint = new DataEndpoint("example", null, EndpointKind.Inline, null,
    new InlineLocation(new[] {
        new object?[] { 1, "Ada" },
        new object?[] { 2, null }
    }));

// Obtain the Arrow schema even when there are no rows.
var arrowSchema = ArrowSchemaMapper.Map(schema);
using var reader = await new ResourceReaderFactory().OpenAsync(endpoint, schema);
foreach (var batch in ArrowRecordBatches.Read(reader, schema,
    new ArrowBatchOptions { BatchSize = 1024, MaxBufferedBytes = 16 * 1024 * 1024, LeaveOpen = true }))
{
    using (batch)
    {
        var ids = (Int64Array)batch.Column(0);
        Console.WriteLine($"{batch.Length} rows; first id: {ids.GetValue(0)}");
    }
}
```

## Supported representations

Fields appear in contract order and retain their canonical names. Reader columns match
`PhysicalName ?? Name` exactly and case-sensitively, independent of the reader's column
order. Missing or duplicate matching columns fail; extra columns are ignored. This is a
projection adapter, so `FieldMatching` does not request whole-dataset validation.

`DataField.Nullable` controls Arrow nullability and permits `null`/`DBNull`. `Required`
describes property presence and does not make values nullable. No text parsing or
culture-dependent coercion is performed. `PhysicalType`, `Format`, extensions, and
quality/domain constraints are not runtime conversion instructions or validation rules;
canonical logical types and the type parameters below determine representation.

| Canonical logical type | Arrow representation | Accepted CLR values / parameters |
| --- | --- | --- |
| `boolean` | Boolean | `bool` |
| `integer` | Int64 by default | All CLR integer types, checked for range; `integerWidth` is an Int32 of 8/16/32/64 and `signed` is a Boolean, default true |
| `number` without precision/scale | Double | `float` or `double`; widening float preserves its value, including NaN/infinity |
| `decimal`, or `number` with precision/scale | Decimal128 | `decimal`; requires Int32 `precision` (1..38) and `scale` (0..precision) constraints |
| `string` | UTF-8 String | `string`; invalid UTF-16 is rejected |
| `binary` | Binary | `byte[]`; copied because readers may reuse buffers |
| `date` | Date32, days since 1970-01-01 | `DateOnly`, or midnight `DateTime` with Utc/Unspecified kind; local times and non-midnight values fail |
| `timestamp`, `datetime` | Timestamp | See units and timezone semantics below |

Decimals exceeding the declared precision or containing nonzero digits beyond the scale
fail rather than round. CLR decimal input has at most 29 significant digits even when the
Arrow schema allows 38. Integer narrowing checks overflow. Decimal/integer values are not
implicitly converted to Double because that can lose information. Objects, arrays, nested
fields, times, missing logical types, unknown logical types, invalid parameters, and
duplicate parameters are explicitly unsupported. Errors name the affected field.

Timestamp `precision` is a string constraint: `second`, `millisecond`, `microsecond`
(default), or `nanosecond`. Values with finer precision than the selected unit fail.
Nanosecond output multiplies CLR ticks by 100 with overflow checking, limiting the range
to that of an Arrow signed 64-bit nanosecond counter; it cannot create sub-tick precision.
Dates before the epoch and negative timestamps are supported.

Timestamp `timezone` defaults to true. True uses Arrow timezone `UTC`, accepting UTC
`DateTime` or `DateTimeOffset` and normalizing offset values to their UTC instant. The
original per-value offset is not retained. False uses an empty Arrow timezone and accepts
only Unspecified `DateTime`, preserving wall-clock components without interpreting a
local zone. Local `DateTime`, mixing naive/aware representations, non-Boolean timezone
parameters, and `defaultTimezone` fail explicitly. Resolve named zones, daylight-saving
ambiguities, or text parsing before conversion.

## Batching, memory, cancellation, and ownership

`BatchSize` (default 1024) limits rows. `MaxBufferedBytes` (default 16 MiB) limits the
logical Arrow payload buffered per batch, conservatively counting validity, values,
and variable-width offsets. A row larger than the budget fails explicitly. A byte-limit
split may read one additional row before yielding. Row-limit splits do not read ahead.
An empty result produces no batches; use `ArrowSchemaMapper.Map` for its schema.

The adapter retains at most one batch of managed rows plus one pending row and constructs
one Arrow batch at a time. Encoding/copying temporarily duplicates that payload. Managed
object references, UTF-16 strings, list capacity, builders, and Arrow allocation padding
add bounded overhead proportional to the configured rows, fields, and payload budget;
the byte limit is not a process-memory limit. Caller-retained batches add memory until
disposed. Upstream readers may materialize entire datasets: the existing Parquet reader
does so, and the inline example also materializes its input. The lazy 100,003-row test
demonstrates bounded adapter buffering, not bounded memory for every source.

Argument and option validation happens when `Read` is called. Ownership starts on the
first enumerator `MoveNext`, including schema validation. By default the adapter disposes
the reader on successful completion, conversion/reader failure, cancellation, and early
enumerator disposal. With `LeaveOpen = true` the caller owns the reader in all cases.
Do not re-enumerate a reader or enumerate it concurrently. Calling `Read` without starting
enumeration does not transfer ownership. Only the current result set is converted.

Every yielded `RecordBatch` belongs to the caller and must be disposed, even on early
exit. Advancing or disposing the enumerator does not dispose yielded batches. Any arrays
constructed for a batch that fails before being yielded are disposed by the adapter.
Use `foreach` or dispose the enumerator explicitly so early exit releases the reader.

Pass a `CancellationToken` to `Read`. Cancellation is checked before/after synchronous
reader calls, between field conversions, and while building arrays. Cancellation cannot
interrupt a blocking synchronous `IDataReader.Read` or `GetValue`, nor an individual
Arrow builder operation. Enumeration and conversion run on the calling thread.
