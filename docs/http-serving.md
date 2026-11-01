---
title: Serve asset data over HTTP
parent: Guides
nav_order: 24
permalink: /http-serving/
---

<!-- cli:data serve -->

Expose explicitly selected contract assets through one read-only HTTP service:

```console
packata data serve contract.yaml --asset orders --binding 0 --port 0
```

The positional `<SOURCE>` is a contract path/URL/archive. `--input-format
auto|odcs|datapackage` selects its standard.

`--asset` is repeatable; `--binding` is an explicit zero-based index applied to
each selected asset. The default address is `127.0.0.1`; port `0` asks the OS for
an available port. Set `--address` and `--port` to bind another IP/port. Ctrl+C
cancels active streams and stops the service. Diagnostics go to stderr. Stdout
contains one JSON readiness record (`event: "ready"`) with the actual `baseUrl`,
asset identities, schema/data URLs, format capabilities and completion retention.
Selection and bind failures produce no readiness record.

`--formats ndjson,avro,arrow` controls enabled encoders (all three by default).
`--batch-size 128` bounds rows per flush/block/batch and
`--maximum-buffer-bytes 1048576` bounds logical row/block/batch buffers. Existing
`--connector`, `--connection-env` and `--staging-maximum-bytes` options apply to
source readers. The source format is independent of the response format. The CLI
registers CSV/TSV/PSV, Excel, Parquet, inline and database readers; embedded hosts
can supply other readers. Source capability limitations are advertised without
reading data where they can be determined from registered capabilities.

<!-- end-cli -->

## Metadata and asset identity

| GET path | Response |
| --- | --- |
| `/` | Protocol identifier `packata-http-v1` and discovery/completion locations |
| `/assets` | All explicitly served selections and their capabilities |
| `/assets/{id}` | One selection's metadata |
| `/assets/{id}/schema` | Declared column names, logical/physical types, nullability, mapping parameters and per-format schemas/limitations |
| `/assets/{id}/data` | The selected representation of the same asset |
| `/transfers/{id}` | Terminal completion state, byte count and SHA-256 (or pending/failed state) |

The opaque asset ID hashes the contract ID/version, asset ID and selected
endpoint/path. Reordering unrelated assets does not change it; changing the
contract version does. Metadata includes the readable contract ID, version,
asset ID and binding index. It never publishes connection strings or source
locations. Metadata is available before opening a source, including one-shot and
empty datasets. No schema inference from data is necessary. Field names and order
come from the contract; physical names select source columns and never rename
output columns. Each GET opens a new reader; a one-shot source must be requested
only once. The service does not cache or replay its data.

Avro format metadata includes the full JSON writer schema. Arrow metadata carries
`schemaIpcBase64`, an encapsulated IPC schema message that standard clients can
decode with `pyarrow.ipc.read_schema(pa.BufferReader(base64.b64decode(value)))`.

## Negotiation

| Representation | Response `Content-Type` | Accepted alias |
| --- | --- | --- |
| UTF-8 NDJSON objects | `application/x-ndjson` | `application/ndjson` |
| Avro Object Container File, null codec | `application/avro` | `avro/binary` |
| Arrow IPC stream | `application/vnd.apache.arrow.stream` | None |

Every data response varies on `Accept`. Missing/blank Accept and unconstrained
wildcards choose NDJSON when available. For each enabled, asset-compatible
encoder, the most specific matching range determines its quality: exact media
type and supported parameters, then type wildcard, then `*/*`. Consequently an
exact `q=0` excludes a representation even under `*/*;q=1`. Aliases refer to the
same representation. Equal-specificity duplicate ranges use the first occurrence.
Candidates then sort by quality, specificity, server preference (NDJSON, Avro,
Arrow), and lexical media type for custom encoders. Weights use HTTP q-value
syntax (0..1, at most three fractional digits). Only `charset=utf-8` is supported
as an additional media parameter, for NDJSON. Unsupported/malformed ranges yield
406 when no representation matches. A request naming only an unavailable format
receives 406 with configured choices and limitations; it cannot silently fall
back. Explicit acceptable alternatives or wildcards can select another format.

## Type mappings

All built-in encoders support flat declared primitive columns. Nested values,
unknown logical types, ambiguous names, implicit local timezone interpretation,
and lossy conversions are rejected. Nulls require `Nullable = true`; `Required`
does not substitute for nullability. A mismatched CLR value produces a field
diagnostic internally and fails the transfer; HTTP errors do not expose provider
messages that may contain secrets.

| Canonical type | NDJSON value | Avro value | Arrow value |
| --- | --- | --- | --- |
| integer | Exact decimal **string**, including values beyond 2^53 | int/long according to width and signedness; unsigned 64-bit unsupported | Declared signed/unsigned width |
| number without precision/scale | Finite JSON number | double | double |
| decimal, or number with precision/scale | Exact decimal string | Signed big-endian bytes with decimal logical type | decimal128 |
| boolean/string | JSON boolean/string, correctly escaped | boolean/string | bool/UTF-8 |
| binary | Base64 string | bytes | binary |
| date | `YYYY-MM-DD` string | date (days since epoch) | date32 |
| timestamp/datetime | ISO 8601; UTC `Z` for zoned values, no suffix for naive values | timestamp-micros or local-timestamp-micros | Declared unit and UTC/naive semantics |

Decimal precision is 1..38 and scale 0..precision; source values must be CLR
`decimal` and fit without rounding. Integers accept integral CLR values only.
Floating-point mappings accept CLR float/double; row encoders reject non-finite
values. Arrow retains its adapter's IEEE float behavior. Source readers can have
narrower legacy mappings: e.g. inline integer columns default to Int32 unless
`Format = "i64"`, and inline floating columns need `Format = "fp64"` to produce
double rather than decimal. No encoder silently repairs a lossy upstream value.

Timestamp precision defaults to microseconds and accepts second, millisecond,
microsecond or nanosecond declarations; row values must satisfy the declared
precision. Zoned inputs require UTC DateTime or DateTimeOffset and normalize to
UTC. `timezone=false` requires Unspecified DateTime and preserves a wall-clock
value. Avro rejects nanosecond declarations and uses microsecond logical types
for the other supported declarations. Dates accept DateOnly or a non-local
midnight DateTime. Avro requires valid field identifiers and never normalizes
names silently. Its record name is `Packata.Http.Assets.Asset_<opaque-id>`, fields
follow contract order, nullable unions are null-first with null defaults, and
the full writer schema appears both in metadata and the container header, even
for empty input. Only the null codec is implemented; raw Avro messages and
schema-registry framing are outside this API.

## Completion and failure protocol

Data response headers contain `Packata-Transfer-Id` and `Packata-Completion`, a
relative URL for that transfer's status. Supported clients must:

1. Require a successful HTTP status and decode incrementally.
2. Count and SHA-256 hash **all raw response bytes** as they are consumed, without
   accumulating the response. Drain through HTTP EOF, including any format end
   marker, and reject transport/decoder errors.
3. GET the completion URL and require `state == "complete"`, the matching asset
   identity, and exact `bytes` and `sha256` matches. `pending`, `failed`, an expired
   or unreachable status, or a mismatch never establishes success.

Rows/batches observed before verification are partial results. A parseable
prefix, Arrow EOS, or EOF alone is insufficient: failure between complete NDJSON
rows or Avro blocks must not be accepted as success. No schema/error records are
injected into data. Do not automatically retry after partial delivery; the caller
must explicitly choose whether to restart and handle already processed rows.

Pre-response source/encoder failures return 422 problem JSON, cancellation can
return 503, and errors after headers abort the response and mark the transfer
failed. Unsupported negotiation returns 406 without opening the source. Unknown
identities return 404. The completion registry retains terminal statuses for ten
minutes by default and caps all retained/pending transfers at 4096; when full,
new data requests return 429 rather than evicting required completion evidence.
Concurrent data requests default to eight; excess requests return 429. Host
restarts invalidate completion evidence.

Lossy/unsupported runtime values include safe `LOSSLESS_CONVERSION_FAILED` and
the declared `field` in pre-response problems or the failed transfer's status.
Provider details and inner exception messages remain private.

## Incremental clients and embedding

The [working Python example](https://github.com/Seddryck/Packata/blob/main/src/Packata.Http.Testing/Interop/validate_http.py)
uses `http.client`, per-line JSON parsing, `fastavro.reader` and
`pyarrow.ipc.open_stream`. Its `CheckedBody` hashes/counts bytes as the decoders
read; it never performs an unbounded read. Arrow is consumed by record batch and
Avro/NDJSON by row. Install `pyarrow`, `fastavro` and timezone data (`tzdata` on
Windows), then run it against the printed base URL. The fixture-specific value
assertions demonstrate expected mappings; adapt those assertions to your asset.

```csharp
using Packata.Http;
using Packata.Http.Arrow; // Optional Packata.Http.Arrow package

var selected = new ServedAsset(contract, "orders", bindingIndex: 0);
await using var host = await AssetHttpHost.StartAsync(
    [selected], readerFactory,
    [new NdjsonRepresentation(), new AvroRepresentation(), new ArrowRepresentation()],
    new AssetHttpOptions { Port = 0, Log = Console.Error.WriteLine }, cancellationToken);
Console.WriteLine(host.Readiness.BaseUrl);
await host.WaitForShutdownAsync(cancellationToken);
```

`Packata.Http` contains the host and independently registered row encoders;
`Packata.Http.Arrow` adds Arrow through the existing export API. The host never
owns the supplied factory, but each encoder owns and disposes every reader it
opens, on completion, exception, cancellation or disconnect. The HTTP body belongs
to the host. Encoders await destination writes/flushes; no producer queue or
whole-dataset collection exists. NDJSON and Avro hold bounded row and block
buffers plus bounded per-value encoding scratch space. Arrow holds one bounded
batch plus adapter/allocator overhead. Very large single rows fail at the byte
limit. Upstream readers may buffer independently: inline readers materialize
data; Excel/Parquet acquisition can stage sources. A synchronous Read/GetValue
already in progress cannot be interrupted. Shutdown requests cancellation and
waits up to five seconds; a non-cooperative synchronous provider can outlive that
timeout until it returns. The source is still disposed by its request afterward.

Framework-dependent CLI/embedded applications require the corresponding ASP.NET
Core shared runtime; self-contained CLI archives include it. This is HTTP, with
no Flight or gRPC dependency. The initial API has no uploads, SQL endpoint,
filtering or query execution.

## Delivery decisions and validation

| Dimension | Evidence and decision |
| --- | --- |
| Libraries | Optional portable managed `Packata.Http` and `Packata.Http.Arrow`; one multi-TFM NuGet identity each, repository release version |
| TFMs | Existing net8.0/net9.0/net10.0 declarations retained; test each host target |
| OS checks | Existing Windows library suite plus focused live HTTP/Python checks on Windows and Linux; no expanded full-suite OS matrix |
| CLI distributions | Existing framework/self-contained RIDs, x64/ARM64, glibc/musl and macOS support retained; ASP.NET runtime replaces the plain runtime image for musl framework smoke tests |
| Generation/tools | No new generated build prerequisites; interoperability uses Python 3.12 with pinned PyArrow/fastavro/tzdata |
| Publishing | Existing tested-artifact NuGet/release jobs and release-only triggers/permissions retained; packaging waits for interoperability validation |

Focused tests live in `Packata.Http.Testing` and `ServeCommandTests`, with a
delayed-header regression in `Packata.Arrow.Testing`. Live Python validation sets
`PACKATA_INTEROP_PYTHON` to the Python executable and runs
`PythonReadersConsumeLiveResponsesIncrementally`; the source pauses until the
client confirms its first decoded row/batch, proving incremental decoding.
Slow-destination tests suspend an awaited write against a 100,000-row lazy source;
10,000-row tests check bounded block writes. Tests also cover negotiation,
schema-only discovery, empty data, precision failures, cancellation, disconnect,
shutdown, source errors after complete blocks, ownership and registry bounds.
Exact local commands and unrun suites are recorded in the pull request.
