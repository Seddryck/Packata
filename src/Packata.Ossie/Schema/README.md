# Apache Ossie Layer 1 schema

Packata.Ossie supports the standalone semantic-model document in version
`0.3.0.dev0`, pinned to Apache Ossie revision
[`4d70c2a1e8ad6675fa8d0819a7c8bf061997e3d1`](https://github.com/apache/ossie/tree/4d70c2a1e8ad6675fa8d0819a7c8bf061997e3d1/core-spec).
The vendored [schema](https://github.com/apache/ossie/blob/4d70c2a1e8ad6675fa8d0819a7c8bf061997e3d1/core-spec/ossie-schema.json)
is embedded in the assembly and used for both YAML and JSON. The upstream
[specification](https://github.com/apache/ossie/blob/4d70c2a1e8ad6675fa8d0819a7c8bf061997e3d1/core-spec/spec.md)
describes the document shape and declaration rules.

`OssieModelSerializer` validates document structure and declaration references
for dataset keys and relationships. Source references and expression bodies
are preserved as text. JSON-compatible values in vendor extension payloads and
additional AI context properties are preserved as `JsonElement` values. YAML
scalars use YAML 1.2 core resolution. Quote values such as `true`, `null`, and
`010` when they represent text.

The schema is distributed under the Apache License, Version 2.0. See
[NOTICE](NOTICE) and the repository [license](../../../LICENSE).
