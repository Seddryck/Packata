# Export asset data as Arrow IPC

`Packata.Arrow` exposes `ArrowIpcExporter` independently of the CLI and any process
or network protocol. Supply a canonical contract, an exact asset ID, a zero-based
endpoint binding index, and an `IDataEndpointReaderFactory` with the required
source capabilities:

```csharp
var exporter = new ArrowIpcExporter(new ResourceReaderFactory());
await exporter.WriteAsync(contract, "orders", 0, destination,
    new ArrowIpcExportOptions { BatchSize = 1024, LeaveOpen = true }, cancellationToken);
await exporter.WriteFileAsync(contract, "orders", 0, "orders.arrows",
    cancellationToken: cancellationToken);
```

Asset and endpoint IDs must resolve uniquely, and the asset must declare a
supported Arrow schema. The selected binding's asset path (falling back to the
asset's physical name, then name), schema, and enclosing contract are passed to
the existing reader factory. Missing or ambiguous selections fail before opening
the source or writing the destination. Binding selection is always explicit.

The exporter writes the declared schema first, then one batch at a time, then the
IPC end marker. Empty datasets still contain the declared schema. `BatchSize`
and `MaxBufferedBytes` have the same limits and conversion rules as
`ArrowRecordBatches`; the exporter disposes each batch before advancing. Source
providers may perform their own buffering.

Caller streams stay open by default. With `LeaveOpen = false`, the exporter closes
the destination on success, failure, or cancellation once selection, schema,
options, and destination validation succeed. Every opened reader is owned and
disposed by the exporter. Success flushes the destination. Exceptions propagate;
failures can leave a partial stream without an end marker. Cancellation is passed
to source acquisition and asynchronous writes and checked between synchronous
reads and conversions; an active synchronous read cannot be interrupted.

`WriteFileAsync` always closes its file and creates it with `FileMode.CreateNew`,
so existing files are never overwritten. A failed export may leave a partial file
for the caller to remove. Its bytes are an **IPC stream**, regardless of filename,
and should be read with `open_stream`, rather than `open_file`:

```python
import pyarrow as pa

with pa.input_stream("orders.arrows") as source:
    reader = pa.ipc.open_stream(source)
    print(reader.schema)
    for batch in reader:
        consume(batch)
```

Run the focused .NET tests and independently verify their generated fixtures:

```text
dotnet test src/Packata.Arrow.Testing --framework net10.0
python src/Packata.Arrow.Testing/Interop/validate_export.py src/Packata.Arrow.Testing/bin/Debug/net10.0/arrow-interop
```

The Python check requires `pyarrow` and a timezone database (`tzdata` on Windows).
It verifies schema types and nullability,
Unicode, nulls, decimals, UTC timestamps, empty input, and multiple record batches.
