# Packata Python client

Install a platform wheel downloaded from a [Packata GitHub Release](https://github.com/Seddryck/Packata/releases):

```sh
python -m pip install ./packata-VERSION-py3-none-PLATFORM.whl
```

Wheels are GitHub Release assets only. There is no PyPI or other central package repository publication, no first-use host/runtime download, and no separate .NET or CLI installation requirement. Pip installs PyArrow (and timezone data on Windows); install these dependencies ahead of time for an offline installation. For Avro also install `fastavro>=1.9,<2`. The source client can consume remote NDJSON using only the Python standard library; release wheels deliberately depend on PyArrow for local reads.

```python
import packata

with packata.connect("http://127.0.0.1:8080") as contract:
    print(
        contract.assets
    )  # declared schemas, formats and mapping limitations; no data read
    with contract.asset("SERVICE_ASSET_ID").rows() as rows:
        for row in rows:
            process(row)

with packata.connect("http://127.0.0.1:8080") as contract:
    with contract.asset("SERVICE_ASSET_ID").rows(format="avro") as rows:
        for row in rows:
            process(row)

with packata.open("contract.yaml") as contract:
    with contract.asset("customers").batches(binding=0) as batches:
        for batch in batches:
            process(batch)
```

HTTP asset IDs are the opaque IDs in discovery, not necessarily the contract asset names. `asset.schema` returns declared column metadata; `asset.metadata` includes representation-specific schemas and limitations. `batches()` negotiates Arrow IPC. `rows(preferences=["avro", "ndjson"])` sends ordered Accept preferences and requires the corresponding decoders before making a request. Unknown formats, unavailable dependencies, 406 responses and unexpected Content-Type values raise; a stream never changes codecs or retries after delivery. Remote bindings are chosen by the service.

Use the stream context manager whenever a loop may stop early or raise. Both streams and contracts expose `close()`. Contract exit closes all owned active streams. Iterator exhaustion checks the service's transfer identity, successful terminal state, byte count and SHA-256; truncated bodies, failed completion, expired completion records, timeouts and disconnects raise even after some values have been delivered. Completion must remain available in the service's retention window. `timeout` (default 30 seconds) is a socket inactivity or local frame timeout, not a total query deadline. Server-side synchronous source reads can remain blocked until the provider returns despite cancellation.

Local operations use the private bundled host over stdin/stdout, without a socket. Versioned JSON-line requests select a declared asset and explicit binding; framed Arrow writes end with a separate successful completion frame. Stderr is reserved for host logs and suppressed by this client to avoid leaking source credentials. Discovery does not open source data. Each local iterator owns a fresh process, which is reaped on completion, early close, error or context exit. Local `rows()` uses Arrow internally; additional local wire encodings are unsupported. `PACKATA_HOST` may specify an absolute executable path for development; no PATH search, machine configuration or downloads are used to locate the host.

Additional client buffers are bounded by `max_buffer_bytes` (default 16 MiB): one NDJSON record, one Avro container block plus one decoded record, or individual Arrow decoder reads and one batch. Local transport additionally queues at most two bounded frames and one producer frame. Avro supports the service's null codec, up to 1,000,000 records per block and 1,024 metadata entries per map chunk; metadata has a cumulative byte cap. Arrow and fastavro allocate decoded Python/native objects in addition to encoded buffers; allocator overhead and retained values are not covered by the byte limit. The Arrow batch byte check occurs after decoding; this is a cooperating-service bound, not a sandbox for hostile Arrow input. Source staging/materialization limits are independent and discoverable on the service. Slow consumers only advance decoding when requested; pipe/socket backpressure bounds additional transport buffering. `list(rows)` and `batch.to_pylist()` explicitly materialize results; there is no implicit full-result helper.

NDJSON reconstructs exact integers, Decimal, base64 bytes, date and ISO timestamps from declared types. Nulls are checked against declared nullability. Decimal precision/scale, integer ranges and timestamp units are enforced by the server. Avro uses its declared logical types via fastavro; Arrow preserves declared schema metadata. Python datetime preserves microseconds and offsets; NDJSON refuses nonzero sub-microsecond digits. Use Arrow batches for nanosecond fidelity. `rows()` on Arrow uses PyArrow's Python scalar conversion, whose timestamp precision behavior depends on available scalar libraries; batches retain full fidelity.

## Supported wheels and runtime policy

| Dimension | Evidence and decision |
|---|---|
| Python | 3.10–3.14; pure Python API, PyArrow installed dependency; CI tests endpoints 3.10 and 3.14 |
| Windows | x64 (`win_amd64`), existing CLI RID `win-x64`, native Windows smoke |
| Linux | x64 glibc 2.35+ (`manylinux_2_35_x86_64`), built/tested on Ubuntu 22.04; .NET 10 native prerequisites required |
| Other platforms | macOS, ARM64, x86, musl/Alpine and Python outside this range have no supported wheel yet; CLI support alone does not validate a Python wheel |
| Host | ordinary self-contained .NET 10 publish, untrimmed, no AOT, includes ASP.NET runtime because the private executable reuses the CLI |
| Version | same source version as other release outputs; PEP 440 alpha/beta/rc numeric conversion, branch prereleases become `.dev0+LABEL` |
| Destination | build on PRs/main; immutable validated wheels attach only to the existing patch-zero GitHub release; no registry credentials or upload step |

The wheel includes Packata's Apache license, resolved NuGet package identities/license metadata and supplied license/notice files, including .NET runtime notices. CI records each wheel's compressed size in its job summary and validates ZIP contents, tags, RECORD hashes and executable permissions. Build with `python python/build_wheel.py --help` after self-contained publishing. Runtime pins come from `Directory.Build.props`; update the .NET 10 SDK/runtime security pins together, rebuild every supported wheel and rerun native offline smoke tests before release. Never patch a wheel in place or download a newer runtime at startup.

Self-contained does not supply every OS or source prerequisite. Linux needs .NET's native system dependencies (including ICU/OpenSSL). Database drivers, provider packs, credentials, authentication tools and remote connectivity may need separate configuration. Local file/inline fixtures need no network after wheel and Python dependencies are installed. Runtime startup itself never downloads. Source connectors follow the CLI's existing configuration and may download provider packages unless configured offline; source requirements are independent of the Python host/runtime bundle.

## Focused validation

```sh
python -m unittest discover -s python/tests -v
```

Set `PACKATA_HOST` to a built executable for local tests. Without a bundled host or override, local tests explicitly skip. CI installs the wheel, unsets the override, and runs local smoke tests with an empty PATH and unusable .NET root. Live server interoperability additionally runs `HttpHostTests.PythonReadersConsumeLiveResponsesIncrementally` with `PACKATA_INTEROP_PYTHON` pointing to the environment containing this package, PyArrow and fastavro. Full .NET suites and cross-platform wheel execution are separate from these focused tests.
