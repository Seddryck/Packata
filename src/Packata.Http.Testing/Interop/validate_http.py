"""Live incremental HTTP interoperability. Requires pyarrow, fastavro and timezone data.

Usage: python validate_http.py http://127.0.0.1:PORT/
Success requires HTTP completion AND the host's terminal byte count and SHA-256.
"""
import base64
import hashlib
import json
import sys
import urllib.request
from datetime import date, datetime, timezone
from decimal import Decimal

import fastavro
import pyarrow as pa


class CheckedBody:
    def __init__(self, response):
        self.response = response
        self.bytes = 0
        self.sha = hashlib.sha256()

    def _count(self, data):
        self.bytes += len(data)
        self.sha.update(data)
        return data

    def read(self, n=-1):
        if n < 0:
            raise AssertionError("The decoder attempted an unbounded read")
        return self._count(self.response.read(n))

    def readline(self):
        return self._count(self.response.readline())

    def readable(self):
        return True

    @property
    def closed(self):
        return self.response.closed


def expected(i):
    return dict(id=9007199254740993 + i, text=None if i == 1 else 'héllo\n"world"',
                amount=None if i == 1 else Decimal("-123.45"),
                instant=None if i == 1 else datetime(2026, 1, 2, 10, tzinfo=timezone.utc),
                day=date(1969, 12, 31), bytes=b"\x00\xff", number=1.5, flag=True)


base = sys.argv[1].rstrip("/")
with urllib.request.urlopen(base + "/assets") as response:
    assets = json.load(response)
media_types = ["application/x-ndjson", "application/avro", "application/vnd.apache.arrow.stream"]
for asset in assets:
    # All format schemas are available before consuming a potentially one-shot source.
    formats = {fmt["mediaType"]: fmt for fmt in asset["formats"]}
    arrow_schema = pa.ipc.read_schema(pa.BufferReader(base64.b64decode(
        formats["application/vnd.apache.arrow.stream"]["schema"]["schemaIpcBase64"])))
    assert arrow_schema.field("amount").type == pa.decimal128(18, 2)
    avro_schema = json.loads(formats["application/avro"]["schema"])
    assert avro_schema["fields"][2]["type"][1]["logicalType"] == "decimal"
    for media in media_types:
        request = urllib.request.Request(base + asset["dataUrl"], headers={"Accept": media})
        with urllib.request.urlopen(request) as response:
            assert response.headers["Content-Type"] == media
            completion_url = base + response.headers["Packata-Completion"]
            body = CheckedBody(response)
            if media == "application/x-ndjson":
                def ndjson():
                    while line := body.readline():
                        row = json.loads(line)
                        row["id"] = int(row["id"])
                        row["amount"] = None if row["amount"] is None else Decimal(row["amount"])
                        row["instant"] = None if row["instant"] is None else datetime.fromisoformat(row["instant"].replace("Z", "+00:00"))
                        row["day"] = date.fromisoformat(row["day"])
                        row["bytes"] = base64.b64decode(row["bytes"])
                        yield row
                rows = ndjson()
            elif media == "application/avro":
                reader = fastavro.reader(body)
                assert reader.writer_schema["fields"][2]["type"][1]["logicalType"] == "decimal"
                rows = iter(reader)
            else:
                reader = pa.ipc.open_stream(body)
                assert reader.schema.field("amount").type == pa.decimal128(18, 2)
                assert reader.schema.field("instant").type == pa.timestamp("us", tz="UTC")
                def arrow_rows():
                    for batch in reader:
                        assert batch.num_rows <= 2
                        yield from batch.to_pylist()
                rows = arrow_rows()
            count = 0
            for row in rows:
                if count == 0:
                    print("first " + media, flush=True)
                assert row == expected(count), (media, row)
                count += 1
            while body.read(8192):
                pass
        assert count == (0 if asset["assetId"] == "empty" else 5), (media, count)
        with urllib.request.urlopen(completion_url) as response:
            completion = json.load(response)
        assert completion["state"] == "complete", completion
        assert completion["assetId"] == asset["id"], completion
        assert completion["bytes"] == body.bytes, completion
        assert completion["sha256"] == body.sha.hexdigest(), completion
print("All three incremental Python readers verified schema, equivalent values, empty data and completion", flush=True)
