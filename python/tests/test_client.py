import base64
from contextlib import contextmanager
from datetime import date, datetime, timezone
from decimal import Decimal
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import sys
import tracemalloc
import unittest
from unittest.mock import patch

import fastavro
import pyarrow as pa
import packata
from packata.client import _Body, _avro, _dependency, _Host


ROW = dict(id=9007199254740993, amount=Decimal("123.45"), day=date(2026, 1, 1),
           instant=datetime(2026, 1, 1, tzinfo=timezone.utc), binary=b"\x00\xff", text=None)
COLUMNS = [dict(name=name, logicalType=kind, nullable=name == "text", constraints=[])
           for name, kind in zip(ROW, ["integer", "decimal", "date", "timestamp", "binary", "string"])]
AVRO = dict(type="record", name="Row", fields=[
    dict(name="id", type="long"),
    dict(name="amount", type=dict(type="bytes", logicalType="decimal", precision=10, scale=2)),
    dict(name="day", type=dict(type="int", logicalType="date")),
    dict(name="instant", type=dict(type="long", logicalType="timestamp-micros")),
    dict(name="binary", type="bytes"), dict(name="text", type=["null", "string"])])


def encode(fmt, rows):
    if fmt == "ndjson":
        def convert(value):
            if isinstance(value, bytes): return base64.b64encode(value).decode()
            return str(value) if isinstance(value, (int, Decimal)) else value.isoformat()
        return b"".join(json.dumps({key: str(value) if key == "id" else value for key, value in row.items()}, default=convert).encode() + b"\n" for row in rows)
    output = io.BytesIO()
    if fmt == "avro":
        fastavro.writer(output, AVRO, rows, sync_interval=1)
    else:
        schema = pa.schema([("id", pa.int64()), ("amount", pa.decimal128(10, 2)), ("day", pa.date32()),
                            ("instant", pa.timestamp("us", "UTC")), ("binary", pa.binary()), ("text", pa.string())])
        with pa.ipc.new_stream(output, schema) as writer:
            for row in rows:
                writer.write_batch(pa.RecordBatch.from_pylist([row], schema=schema))
    return output.getvalue()


@contextmanager
def service(fmt="ndjson", rows=None, mode="complete", gate=None):
    payload = encode(fmt, [ROW, ROW] if rows is None else rows)
    accepts = []
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass
        def do_GET(self):
            if self.path == "/": data = dict(protocol="packata-http-v1")
            elif self.path == "/assets": data = [dict(id="customers", columns=COLUMNS, formats=[])]
            elif self.path == "/transfers/abc":
                data = dict(id="abc", assetId="customers", state="failed" if mode == "failed" else "complete",
                            bytes=len(payload), sha256="bad" if mode == "digest" else hashlib.sha256(payload).hexdigest())
            else:
                accepts.append(self.headers["Accept"])
                if mode == "406":
                    self.send_response(406); self.end_headers(); return
                self.send_response(200)
                from packata.client import MEDIA
                self.send_header("Content-Type", "text/plain" if mode == "type" else MEDIA[fmt])
                if mode != "no-completion":
                    self.send_header("Packata-Transfer-Id", "abc")
                    self.send_header("Packata-Completion", "/transfers/abc")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                try:
                    if gate:
                        if fmt == "ndjson": cut = payload.index(b"\n") + 1
                        elif fmt == "arrow": cut = len(encode("arrow", [ROW])) - 8
                        else:
                            # With one record per block the second block is the suffix before the final sync.
                            cut = len(payload) - (len(payload) - len(encode("avro", [ROW])))
                        self.wfile.write(payload[:cut]); self.wfile.flush()
                        gate.wait(5)
                        self.wfile.write(payload[cut:])
                    elif mode == "truncate": self.wfile.write(payload[:-3])
                    else: self.wfile.write(payload)
                except (BrokenPipeError, ConnectionResetError): pass
                return
            raw = json.dumps(data).encode()
            self.send_response(200); self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw))); self.end_headers(); self.wfile.write(raw)
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    try: yield f"http://127.0.0.1:{server.server_port}", accepts
    finally:
        if gate: gate.set()
        server.shutdown(); server.server_close(); thread.join()


class HttpTests(unittest.TestCase):
    def test_ndjson_runs_without_site_packages(self):
        with service() as (url, _):
            source = Path(__file__).resolve().parents[1] / "src"
            code = "import sys; sys.path.insert(0, sys.argv[1]); import packata; c=packata.connect(sys.argv[2]); assert sum(1 for _ in c.asset('customers').rows()) == 2; c.close()"
            result = subprocess.run([sys.executable, "-S", "-c", code, str(source), url], capture_output=True, timeout=10)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_cross_format_values_multiple_blocks_and_schema(self):
        for fmt in ("ndjson", "avro", "arrow"):
            with self.subTest(fmt=fmt), service(fmt) as (url, accepts), packata.connect(url) as contract:
                self.assertEqual(contract.asset("customers").schema, COLUMNS)
                with contract.asset("customers").rows(fmt) as rows:
                    self.assertEqual(list(rows), [ROW, ROW])
                self.assertIn("application/", accepts[0])
                self.assertFalse(contract._streams)

    def test_empty_all_formats(self):
        for fmt in ("ndjson", "avro", "arrow"):
            with service(fmt, rows=[]) as (url, _), packata.connect(url) as contract:
                self.assertEqual(list(contract.asset("customers").rows(fmt)), [])

    def test_gated_source_yields_before_source_finishes(self):
        for fmt in ("ndjson", "avro", "arrow"):
            gate = threading.Event()
            with self.subTest(fmt=fmt), service(fmt, gate=gate) as (url, _), packata.connect(url, timeout=1) as contract:
                with contract.asset("customers").rows(fmt) as stream:
                    self.assertEqual(next(stream), ROW)
                    self.assertFalse(gate.is_set())
                    gate.set()
                    self.assertEqual(list(stream), [ROW])

    def test_failures_never_succeed_at_exhaustion(self):
        for fmt in ("ndjson", "avro", "arrow"):
            for mode in ("failed", "digest", "truncate", "no-completion", "type", "406"):
                with self.subTest(fmt=fmt, mode=mode), service(fmt, mode=mode) as (url, _), packata.connect(url) as contract:
                    with self.assertRaises(Exception): list(contract.asset("customers").rows(fmt))
                    self.assertFalse(contract._streams)

    def test_preferences_and_batches(self):
        with service("arrow") as (url, accepts), packata.connect(url) as contract:
            self.assertEqual(len(list(contract.asset("customers").batches())), 2)
            self.assertEqual(len(list(contract.asset("customers").rows(preferences=["arrow", "ndjson"]))), 2)
            self.assertIn("q=0.9", accepts[-1])

    def test_close_exception_timeout_and_owner_exit(self):
        for timeout in (False, True):
            gate = threading.Event()
            with service(gate=gate) as (url, _), packata.connect(url, timeout=0.1) as contract:
                stream = contract.asset("customers").rows()
                next(stream)
                if timeout:
                    with self.assertRaises(TimeoutError): next(stream)
                else: contract.close()
                self.assertTrue(stream.closed)
                self.assertFalse(contract._streams)

    def test_bounds_and_missing_dependency(self):
        with service(rows=[dict(ROW, text="x" * 10000)]) as (url, _), packata.connect(url, max_buffer_bytes=4096) as contract:
            with self.assertRaisesRegex(packata.PackataError, "record exceeds"): list(contract.asset("customers").rows())
        with patch("packata.client.importlib.import_module", side_effect=ImportError):
            with self.assertRaisesRegex(packata.PackataError, "Install fastavro"): _dependency("fastavro")
        with self.assertRaises(packata.PackataError): _Body(io.BytesIO(), 1).read(2)

    def test_large_source_slow_consumer_does_not_collect_result(self):
        for fmt in ("ndjson", "avro", "arrow"):
            # Server payload exists before tracing: measure extra client memory only.
            with self.subTest(fmt=fmt), service(fmt, rows=[ROW] * 10000) as (url, _), packata.connect(url) as contract:
                tracemalloc.start()
                try:
                    with contract.asset("customers").rows(fmt) as stream:
                        for _ in range(5):
                            self.assertEqual(next(stream), ROW)
                            time.sleep(0.01)
                        _, peak = tracemalloc.get_traced_memory()
                    self.assertLess(peak, 2 * 1024 * 1024)
                finally: tracemalloc.stop()

    def test_close_interrupts_active_socket_read(self):
        gate = threading.Event()
        with service(gate=gate) as (url, _), packata.connect(url, timeout=10) as contract:
            stream = contract.asset("customers").rows()
            next(stream)
            stopped = threading.Event()
            def consume():
                try: next(stream)
                except Exception: pass
                finally: stopped.set()
            worker = threading.Thread(target=consume, daemon=True); worker.start()
            time.sleep(0.05)
            contract.close()
            self.assertTrue(stopped.wait(1), "close must interrupt the active read")
            worker.join(timeout=1)


@unittest.skipUnless(os.environ.get("PACKATA_HOST") or (Path(packata.__file__).parent / "_host").exists(), "requires built host")
class LocalTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.path = Path(self.directory.name) / "datapackage.json"
        self.path.write_text(json.dumps(dict(name="fixture", resources=[dict(name="customers", data=[dict(id=i, text=None) for i in range(400)],
            schema=dict(fields=[dict(name="id", type="integer"), dict(name="text", type="string")]))])))

    def tearDown(self): self.directory.cleanup()

    def test_local_discovery_multiple_batches_and_repeat(self):
        with packata.open(self.path) as contract:
            self.assertEqual(contract.assets[0]["id"], "customers")
            self.assertGreater(len(list(contract.asset("customers").batches())), 1)
            self.assertEqual(len(list(contract.asset("customers").rows())), 400)

    def test_early_close_and_exceptions_reap_process(self):
        hosts = []
        def create(*args):
            host = _Host(*args); hosts.append(host); return host
        with patch("packata.client._Host", side_effect=create):
            with packata.open(self.path) as contract:
                with self.assertRaises(ValueError):
                    with contract.asset("customers").batches() as stream:
                        next(stream)
                        raise ValueError("consumer failed")
                self.assertTrue(stream.closed)
                self.assertEqual(len(list(contract.asset("customers").rows())), 400)
        self.assertTrue(all(host.process.returncode is not None and not host.worker.is_alive() for host in hosts))

    def test_startup_failure_and_invalid_binding(self):
        with self.assertRaises(packata.PackataError): packata.open(self.path.with_name("absent.json"))
        with packata.open(self.path) as contract:
            with self.assertRaises(packata.PackataError): list(contract.asset("customers").batches(binding=99))

    def test_protocol_error_and_shutdown(self):
        host = _Host(self.path, 5, 16*1024*1024)
        try:
            host.ready(); host.request("invalid")
            with self.assertRaises(packata.PackataError): host.frame()
        finally: host.close()
        self.assertIsNotNone(host.process.returncode)
        self.assertFalse(host.worker.is_alive())

    def test_process_exit_and_local_timeout_cleanup(self):
        host = _Host(self.path, 5, 16*1024*1024)
        try:
            host.ready(); host.process.kill()
            with self.assertRaises(packata.PackataError): host.frame()
        finally: host.close()
        host = _Host(self.path, 5, 16*1024*1024)
        try:
            host.ready()
            host.timeout = 0.05  # host is waiting for a request; no frame can arrive
            with self.assertRaisesRegex(packata.PackataError, "Timed out"): host.frame()
        finally: host.close()
        self.assertIsNotNone(host.process.returncode)
        self.assertFalse(host.worker.is_alive())


if __name__ == "__main__": unittest.main()
