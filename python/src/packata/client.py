import base64
import hashlib
import http.client
import importlib
import io
import json
import os
from pathlib import Path
import queue
import socket
import struct
import subprocess
import threading
from datetime import date, datetime
from decimal import Decimal
from urllib.parse import quote, urlsplit


class PackataError(RuntimeError):
    """Protocol, representation, source or completion failure; never retried."""


class _ResponseOwner:
    def __init__(self, connection, response, transport):
        self.connection, self.response, self.transport = connection, response, transport

    def close(self):
        # shutdown interrupts an active read even if HTTPConnection detached a close-delimited socket.
        try:
            self.transport.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.response.close()
        self.connection.close()


MEDIA = {
    "ndjson": "application/x-ndjson",
    "avro": "application/avro",
    "arrow": "application/vnd.apache.arrow.stream",
}
ALIASES = {"application/ndjson": "ndjson", "avro/binary": "avro"}


def _dependency(name):
    try:
        return importlib.import_module(name)
    except ImportError as error:
        raise PackataError(f"Install {name} to decode this representation") from error


class _Body:
    def __init__(self, raw, limit):
        self.raw, self.limit = raw, limit
        self.bytes, self.sha = 0, hashlib.sha256()
        self.tail = b""
        self.closed = False

    def read(self, size=-1):
        if size < 0 or size > self.limit:
            raise PackataError("Decoder read exceeds max_buffer_bytes")
        data = self.raw.read(size)
        self.bytes += len(data)
        self.sha.update(data)
        self.tail = (self.tail + data)[-8:]
        return data

    def readable(self):
        return True

    def close(self):
        self.closed = True


def _exact(body, size):
    if size < 0 or size > body.limit:
        raise PackataError("Record/block exceeds max_buffer_bytes")
    result = bytearray()
    while len(result) < size:
        part = body.read(size - len(result))
        if not part:
            raise PackataError("Truncated stream")
        result.extend(part)
    return bytes(result)


def _long(body, first=None):
    result = 0
    for index in range(10):
        value = (first if index == 0 and first is not None else _exact(body, 1))[0]
        if index == 9 and value > 1:
            raise PackataError("Invalid Avro integer")
        result |= (value & 127) << (7 * index)
        if not value & 128:
            return (result >> 1) ^ -(result & 1)
    raise PackataError("Invalid Avro integer")


def _metadata_count(body):
    count = _long(body)
    if count < 0:
        count = -count
        size = _long(body)
        if not 0 <= size <= body.limit:
            raise PackataError("Avro metadata block exceeds limit")
    if count > 1024:
        raise PackataError("Too many Avro metadata entries")
    return count


def _avro_metadata(body):
    metadata, total = {}, 0
    while count := _metadata_count(body):
        for _ in range(count):
            key = _exact(body, _long(body)).decode("utf-8")
            value = _exact(body, _long(body))
            total += len(key) + len(value)
            if total > body.limit:
                raise PackataError("Avro metadata exceeds limit")
            metadata[key] = value
    return metadata


def _avro(body):
    avro = _dependency("fastavro")
    if _exact(body, 4) != b"Obj\x01":
        raise PackataError("Invalid Avro container")
    metadata = _avro_metadata(body)
    if metadata.get("avro.codec", b"null") != b"null":
        raise PackataError("Only the service's Avro null codec is supported")
    schema = avro.parse_schema(json.loads(metadata["avro.schema"]))
    sync = _exact(body, 16)
    while first := body.read(1):
        count = _long(body, first)
        if not 0 < count <= 1_000_000:
            raise PackataError("Avro block count exceeds 1,000,000")
        block = io.BytesIO(_exact(body, _long(body)))
        if _exact(body, 16) != sync:
            raise PackataError("Invalid Avro sync marker")
        for _ in range(count):
            yield avro.schemaless_reader(block, schema)
        if block.read(1):
            raise PackataError("Unexpected bytes after Avro block")


def _logical(row, columns):
    for column in columns:
        name, kind = column["name"], (column.get("logicalType") or "").lower()
        value = row[name]
        if value is None:
            if not column["nullable"]:
                raise PackataError(f"Null in non-nullable column {name}")
            continue
        constraints = {
            item["kind"]: item["value"] for item in column.get("constraints", [])
        }
        row[name] = _logical_value(value, kind, constraints)
    return row


def _logical_value(value, kind, constraints):
    if kind == "integer":
        return int(value)
    if kind == "decimal" or kind == "number" and "scale" in constraints:
        return Decimal(value)
    if kind == "binary":
        return base64.b64decode(value, validate=True)
    if kind == "date":
        return date.fromisoformat(value)
    if kind in ("datetime", "timestamp"):
        return _timestamp(value)
    return value


def _timestamp(value):
    # The service emits CLR ticks. Refuse to silently lose sub-microsecond ticks.
    fraction = value.split(".", 1)[1] if "." in value else ""
    digits = fraction.split("+", 1)[0].split("-", 1)[0].rstrip("Z")
    if len(digits) > 6 and any(char != "0" for char in digits[6:]):
        raise PackataError("Use Arrow batches to preserve sub-microsecond timestamps")
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def _ndjson(body, columns):
    # Bytewise readline avoids socket read(size) waiting for a complete buffer before yielding.
    line = bytearray()
    while byte := body.read(1):
        line.extend(byte)
        if len(line) > body.limit:
            raise PackataError("NDJSON record exceeds max_buffer_bytes")
        if byte == b"\n":
            yield _logical(json.loads(line), columns)
            line.clear()
    if line:
        raise PackataError("Truncated NDJSON record (missing newline)")


def _arrow(body, batches, limit):
    with _dependency("pyarrow").ipc.open_stream(body) as reader:
        for batch in reader:
            if batch.nbytes > limit:
                raise PackataError("Arrow batch exceeds max_buffer_bytes")
            if batches:
                yield batch
            else:
                yield from batch.to_pylist()
    if body.read(1):
        raise PackataError("Unexpected data after Arrow end marker")
    if body.tail != b"\xff\xff\xff\xff\x00\x00\x00\x00":
        raise PackataError("Missing Arrow end marker")


class Stream:
    def __init__(self, owner, generator):
        self.owner, self.generator = owner, generator
        self.cleanup = None
        self.closed = False
        owner._streams.add(self)

    def __iter__(self):
        return self

    def __next__(self):
        if self.closed:
            raise StopIteration
        try:
            return next(self.generator)
        except BaseException:
            self.close()
            raise

    def close(self):
        if not self.closed:
            self.closed = True
            if self.cleanup:
                self.cleanup()
            try:
                self.generator.close()
            except ValueError:
                # A concurrent read unwinds after cleanup cancels its socket/process.
                pass
            self.owner._streams.discard(self)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()


class Asset:
    def __init__(self, owner, metadata):
        self.owner, self.metadata = owner, metadata

    @property
    def schema(self):
        return self.metadata["columns"]

    def rows(self, format="ndjson", *, preferences=None, binding=0):
        return self.owner._stream(self, preferences or [format], False, binding)

    def batches(self, *, binding=0):
        return self.owner._stream(self, ["arrow"], True, binding)


class Contract:
    def __init__(self, timeout, max_buffer_bytes):
        if timeout <= 0 or max_buffer_bytes <= 0:
            raise ValueError("Timeout and buffer limit must be positive")
        self.timeout, self.limit = timeout, max_buffer_bytes
        self._streams, self.closed = set(), False

    def asset(self, name):
        self._check()
        try:
            return Asset(
                self, next(asset for asset in self.assets if asset["id"] == name)
            )
        except StopIteration as error:
            raise PackataError(f"Unknown asset {name!r}") from error

    def _check(self):
        if self.closed:
            raise PackataError("Contract is closed")

    def close(self):
        self.closed = True
        for stream in tuple(self._streams):
            stream.close()

    def __enter__(self):
        self._check()
        return self

    def __exit__(self, *args):
        self.close()


class Remote(Contract):
    def __init__(self, url, **options):
        super().__init__(**options)
        self.url = urlsplit(url)
        if (
            self.url.scheme not in ("http", "https")
            or not self.url.hostname
            or self.url.username
            or self.url.query
            or self.url.fragment
        ):
            raise ValueError(
                "Use an HTTP(S) service URL without credentials, query or fragment"
            )
        if self._json("/").get("protocol") != "packata-http-v1":
            raise PackataError("Unsupported HTTP protocol")
        self.assets = self._json("/assets")

    def _request(self, path, accept="application/json"):
        if not path.startswith("/") or path.startswith("//") or urlsplit(path).netloc:
            raise PackataError("Invalid service-relative URL")
        cls = (
            http.client.HTTPSConnection
            if self.url.scheme == "https"
            else http.client.HTTPConnection
        )
        connection = cls(self.url.hostname, self.url.port, timeout=self.timeout)
        try:
            connection.request(
                "GET",
                self.url.path.rstrip("/") + path,
                headers={"Accept": accept, "Accept-Encoding": "identity"},
            )
            transport = connection.sock
            response = connection.getresponse()
            if response.status != 200:
                raise PackataError(
                    f"HTTP {response.status}: check asset, enabled formats, schema and source prerequisites"
                )
            if response.getheader("Content-Encoding", "identity") != "identity":
                raise PackataError("Unexpected HTTP content encoding")
            return _ResponseOwner(connection, response, transport), response
        except BaseException:
            connection.close()
            raise

    def _json(self, path):
        connection, response = self._request(path)
        try:
            if (
                response.getheader("Content-Type", "").split(";", 1)[0]
                != "application/json"
            ):
                raise PackataError("Expected JSON metadata")
            data = response.read(self.limit + 1)
            if len(data) > self.limit:
                raise PackataError("Metadata exceeds max_buffer_bytes")
            return json.loads(data)
        finally:
            connection.close()

    def _stream(self, asset, formats, batches, binding):
        self._check()
        if binding != 0:
            raise PackataError("Remote bindings are selected by the service")
        if (
            not formats
            or len(formats) > 3
            or len(set(formats)) != len(formats)
            or any(fmt not in MEDIA for fmt in formats)
        ):
            raise ValueError("Choose unique formats from ndjson, avro, arrow")
        for fmt in formats:
            if fmt == "arrow":
                _dependency("pyarrow")
            if fmt == "avro":
                _dependency("fastavro")
        accept = ", ".join(
            f"{MEDIA[fmt]};q={1 - index / 10:.1f}" for index, fmt in enumerate(formats)
        )
        stream = Stream(self, iter(()))

        stream.generator = self._iterate(asset, formats, batches, accept, stream)
        return stream

    def _iterate(self, asset, formats, batches, accept, stream):
        connection, response = self._request(
            "/assets/" + quote(asset.metadata["id"], safe="") + "/data", accept
        )
        stream.cleanup = connection.close
        try:
            media = (
                response.getheader("Content-Type", "").split(";", 1)[0].strip().lower()
            )
            fmt = next(
                (key for key, value in MEDIA.items() if value == media),
                ALIASES.get(media),
            )
            if fmt not in formats:
                raise PackataError("Unexpected Content-Type for requested formats")
            completion = response.getheader("Packata-Completion", "")
            transfer_id = response.getheader("Packata-Transfer-Id", "")
            if not transfer_id or completion != "/transfers/" + transfer_id:
                raise PackataError("Missing or invalid completion protocol")
            body = _Body(response, self.limit)
            yield from self._decode(body, fmt, batches, asset.schema)
            status = self._json(completion)
            if (
                status.get("id"),
                status.get("assetId"),
                status.get("state"),
                status.get("bytes"),
                status.get("sha256"),
            ) != (
                transfer_id,
                asset.metadata["id"],
                "complete",
                body.bytes,
                body.sha.hexdigest(),
            ):
                raise PackataError(
                    "Transfer did not complete with matching length and SHA-256"
                )
        finally:
            connection.close()

    def _decode(self, body, fmt, batches, columns):
        if fmt == "ndjson":
            yield from _ndjson(body, columns)
        elif fmt == "avro":
            yield from _avro(body)
        else:
            yield from _arrow(body, batches, self.limit)


class _Host:
    def __init__(self, path, timeout, limit):
        override = os.environ.get("PACKATA_HOST")
        executable_name = "packata.exe" if os.name == "nt" else "packata"
        executable = Path(__file__).parent / "_host" / executable_name
        if override:
            executable = Path(override)
        if not executable.is_absolute():
            raise PackataError("PACKATA_HOST must be an absolute executable path")
        if not executable.is_file():
            raise PackataError(
                "Bundled host missing; install a supported GitHub Release wheel or set PACKATA_HOST"
            )
        command = [str(executable), "--python-host", str(path)]
        self.process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        self.timeout, self.limit = timeout, limit
        self.queue, self.stopping = queue.Queue(maxsize=2), threading.Event()
        self.worker = threading.Thread(target=self._pump, daemon=True)
        self.worker.start()

    def _pump(self):
        try:
            while not self.stopping.is_set():
                header = self.process.stdout.read(5)
                if len(header) != 5:
                    raise PackataError("Host exited before completion")
                size = struct.unpack("<I", header[1:])[0]
                if size > self.limit:
                    raise PackataError("Host frame exceeds max_buffer_bytes")
                data = bytearray()
                while len(data) < size:
                    part = self.process.stdout.read(size - len(data))
                    if not part:
                        raise PackataError("Truncated host frame")
                    data.extend(part)
                if not self._put((header[:1], bytes(data))):
                    return
        except Exception as error:
            self._put(error)

    def _put(self, value):
        while not self.stopping.is_set():
            try:
                self.queue.put(value, timeout=0.1)
                return True
            except queue.Full:
                pass
        return False

    def frame(self):
        try:
            value = self.queue.get(timeout=self.timeout)
        except queue.Empty as error:
            self.close()
            raise PackataError("Timed out waiting for host") from error
        if isinstance(value, Exception):
            raise value
        if value[0] == b"E":
            raise PackataError(
                "Local host failed: check contract, binding, source prerequisites and Arrow schema"
            )
        return value

    def request(self, operation, **kwargs):
        self.process.stdin.write(
            json.dumps(
                dict(protocol="packata-pipe-v1", operation=operation, **kwargs)
            ).encode()
            + b"\n"
        )
        self.process.stdin.flush()

    def ready(self):
        kind, data = self.frame()
        metadata = json.loads(data)
        if kind != b"J" or metadata.get("protocol") != "packata-pipe-v1":
            raise PackataError("Unsupported local host protocol")
        return metadata["assets"]

    def close(self):
        self.stopping.set()
        if self.process.poll() is None:
            self.process.kill()
        self.process.wait(timeout=5)
        self.worker.join(timeout=5)
        for pipe in (self.process.stdin, self.process.stdout):
            pipe.close()


class _PipeBody:
    def __init__(self, host):
        self.host, self.pending, self.complete = host, b"", False

    def read(self, size):
        if self.complete:
            return b""
        result = bytearray()
        while len(result) < size:
            if not self.pending:
                kind, data = self.host.frame()
                if kind == b"C":
                    if data:
                        raise PackataError("Invalid completion frame")
                    self.complete = True
                    break
                if kind != b"D":
                    raise PackataError("Unexpected host frame")
                self.pending = data
            count = min(len(self.pending), size - len(result))
            result.extend(self.pending[:count])
            self.pending = self.pending[count:]
        return bytes(result)


class Local(Contract):
    def __init__(self, path, **options):
        super().__init__(**options)
        self.path = Path(path).resolve()
        host = _Host(self.path, self.timeout, self.limit)
        try:
            self.assets = host.ready()
            host.request("shutdown")
            if host.process.wait(timeout=self.timeout) != 0:
                raise PackataError("Local discovery failed")
        finally:
            host.close()

    def _stream(self, asset, formats, batches, binding):
        self._check()
        # Local transport always uses Arrow; rows() keeps the same default API.
        if formats not in (["arrow"], ["ndjson"]):
            raise PackataError("Local transport supports Arrow only")
        _dependency("pyarrow")
        stream = Stream(self, iter(()))

        def iterate():
            host = _Host(self.path, self.timeout, self.limit)
            stream.cleanup = host.close
            try:
                host.ready()
                host.request("read", asset=asset.metadata["id"], binding=binding)
                raw = _PipeBody(host)
                body = _Body(raw, self.limit)
                yield from _arrow(body, batches, self.limit)
                if not raw.complete:
                    raise PackataError("Local stream lacks completion")
                host.request("shutdown")
                if host.process.wait(timeout=self.timeout) != 0:
                    raise PackataError("Host exited unsuccessfully")
            finally:
                host.close()

        stream.generator = iterate()
        return stream


def connect(url, *, timeout=30, max_buffer_bytes=16 * 1024 * 1024):
    return Remote(url, timeout=timeout, max_buffer_bytes=max_buffer_bytes)


def open(path, *, timeout=30, max_buffer_bytes=16 * 1024 * 1024):
    return Local(path, timeout=timeout, max_buffer_bytes=max_buffer_bytes)
