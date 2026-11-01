"""Run the public package against the actual gated .NET HTTP writers."""
import base64
from datetime import date, datetime, timezone
from decimal import Decimal
import json
import sys
import packata
import pyarrow as pa


def expected(i):
    return dict(id=9007199254740993 + i, text=None if i == 1 else 'héllo\n"world"',
                amount=None if i == 1 else Decimal("-123.45"),
                instant=None if i == 1 else datetime(2026, 1, 2, 10, tzinfo=timezone.utc),
                day=date(1969, 12, 31), bytes=b"\x00\xff", number=1.5, flag=True)


with packata.connect(sys.argv[1], timeout=10) as contract:
    for metadata in contract.assets:
        asset = contract.asset(metadata["id"])
        formats = {fmt["mediaType"]: fmt for fmt in metadata["formats"]}
        schema = pa.ipc.read_schema(pa.BufferReader(base64.b64decode(
            formats["application/vnd.apache.arrow.stream"]["schema"]["schemaIpcBase64"])))
        assert schema.field("amount").type == pa.decimal128(18, 2)
        assert json.loads(formats["application/avro"]["schema"])["fields"][2]["type"][1]["logicalType"] == "decimal"
        for fmt in ("ndjson", "avro", "arrow"):
            count = 0
            with (asset.batches() if fmt == "arrow" else asset.rows(fmt)) as stream:
                for item in stream:
                    if count == 0: print("first " + fmt, flush=True)
                    if fmt == "arrow":
                        assert item.num_rows <= 2
                        rows = item.to_pylist()
                    else: rows = [item]
                    for row in rows:
                        assert row == expected(count), (fmt, row)
                        count += 1
            assert count == (0 if metadata["assetId"] == "empty" else 5)
print("Public Python client verified all live representations and completion", flush=True)
