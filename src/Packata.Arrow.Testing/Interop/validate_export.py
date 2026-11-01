"""Validate fixtures emitted by ArrowIpcExporterTests with an independent Arrow client.

Usage: python validate_export.py <test-output-directory>/arrow-interop
Requires pyarrow; no Python dependency is needed to run the .NET tests.
"""
import sys
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path

import pyarrow as pa

expected_schema = pa.schema([
    pa.field("id", pa.int64(), nullable=False),
    pa.field("text", pa.string()),
    pa.field("amount", pa.decimal128(8, 2)),
    pa.field("instant", pa.timestamp("us", tz="UTC")),
])

for count in (0, 5):
    with pa.input_stream(str(Path(sys.argv[1]) / f"rows-{count}.arrows")) as source:
        reader = pa.ipc.open_stream(source)
        assert reader.schema.equals(expected_schema), reader.schema
        batches = list(reader)
    assert [batch.num_rows for batch in batches] == ([] if count == 0 else [2, 2, 1])
    rows = pa.Table.from_batches(batches, schema=reader.schema).to_pylist()
    assert rows == [
        {"id": i, "text": None if i == 1 else "héllo",
         "amount": None if i == 1 else Decimal("-123.45"),
         "instant": None if i == 1 else datetime(2026, 1, 2, 10, tzinfo=timezone.utc)}
        for i in range(count)
    ], rows
print(f"PyArrow {pa.__version__}: schema, nulls, decimals, timestamps, empty input, and multiple batches passed")
