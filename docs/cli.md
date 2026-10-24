# Packata command-line interface

Install the .NET tool with `dotnet tool install --global Packata-cli`. Run
`packata --help` or `packata --version` to discover commands and the installed version.
The existing `packata infer` command remains available.

## Validate a contract

```console
packata contract validate contract.yaml
packata contract validate datapackage.json --output json
packata contract validate descriptor --format odcs
```

Validation accepts ODCS v3.2.0 YAML and Data Package v2 JSON/YAML. Detection examines
the document root; `--format auto|odcs|datapackage` overrides standard detection.
Local files, HTTP(S) descriptors, and local/HTTP(S) ZIP packages are supported.
A ZIP must contain exactly one descriptor named `datapackage.json`,
`datapackage.yaml`, `datapackage.yml`, `contract.yaml`, or `contract.yml`, including
inside a subdirectory. Validation never opens referenced data resources.

Data Package validation uses the library's descriptor validator. ODCS validation
uses the library's YAML/semantic validation plus checks for required identity,
version, status, supported API version, and document kind.

`--output text` is the default. Text diagnostics go to stderr. `--output json`
writes a single object to stdout with `schemaVersion: 1`, `source`, `valid`, and
`diagnostics`. Each diagnostic has `code`, `severity`, `sourcePath`, and `message`.
Diagnostics do not include raw parser snippets or stack traces; connection secrets
are redacted. Redirected output has no ANSI styling.

Exit codes for these new commands: `0` success, `1` invalid document or operation,
`2` invocation error, `3` I/O failure, and `130` cancellation. These do not change
the existing `infer` command's exit-code contract.
