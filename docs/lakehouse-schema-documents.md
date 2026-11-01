# Lakehouse schema documents

Export one canonical ODCS or Data Package asset without reading rows:

```sh
packata schema export contract.yaml --asset orders --output-format iceberg-json -o orders.iceberg.schema.json
packata schema export contract.yaml --asset orders --output-format paimon-json -o orders.paimon.schema.json
packata schema export contract.yaml --asset orders --output-format avro --profile hudi -o orders.hudi.avsc
packata schema export contract.yaml --asset orders --output-format spark-json --profile delta -o orders.delta.schema.json
```

Omit --asset only when exactly one eligible schema exists. Omit --profile for generic Avro/Spark output. Profiles never change the encoding or infer selectors from a filename. Generation options, unknown profiles, and incompatible profile/format combinations fail before output publication. Stdout contains only the document; diagnostics go to stderr. Existing files, including the source, are protected, and failed/cancelled exports publish no partial artifact.

VS Code offers **Export Schema > Iceberg JSON…** and **Paimon JSON…**. Avro and Spark JSON offer a Generic/Hudi or Generic/Delta profile picker based on the Application catalog. No language, dialect, connection or additional submenu is needed. Text opens as an untitled preview for explicit save. CLI, LSP and VS Code share selection, capabilities and validation; no CLI process is spawned by the editor.

## Supported baselines

| Selection | Baseline and consumer | Boundary |
| --- | --- | --- |
| iceberg-json | Iceberg table spec v2, Appendix C; Java iceberg-core 1.9.2 SchemaParser.fromJson | A schema object, with schema-id (default 0); not table metadata or a table-format version declaration |
| paimon-json | Paimon 1.2.0 TableSchema.fromJson, schema JSON format version 3 | Schema revision id defaults to 0; format version and revision are independent |
| avro + hudi | Hudi 1.0.2, table version 8; Spark 3.5 integration; Avro 1.12 JSON | A conservative engine-compatible initial-schema subset, not every type supported by Hudi's other engines |
| spark-json + delta | Delta Lake 3.3.0 / Spark 3.5; reader 1, writer 2, column mapping none, no table features | The schema object used in metadata.schemaString; no metadata/protocol action or transaction-log commit |

References: [Iceberg serialization](https://iceberg.apache.org/spec/#appendix-c-json-serialization), [Iceberg 1.9.2 parser](https://github.com/apache/iceberg/blob/apache-iceberg-1.9.2/core/src/main/java/org/apache/iceberg/SchemaParser.java), [Paimon schema files](https://paimon.apache.org/docs/2.1/concepts/spec/schema/), [Paimon pinned parser](https://github.com/apache/paimon/blob/release-1.2.0/paimon-api/src/main/java/org/apache/paimon/types/DataTypeJsonParser.java), [Hudi schema evolution](https://hudi.apache.org/docs/1.0.2/schema_evolution/), and [Delta 3.3 protocol](https://github.com/delta-io/delta/blob/v3.3.0/PROTOCOL.md#schema-serialization-format).

## Type mappings and losses

| Canonical type | Iceberg v2 | Paimon format 3 |
| --- | --- | --- |
| boolean, string | boolean, string | BOOLEAN, STRING |
| signed integer | int through 32 bits, long through 64 | TINYINT, SMALLINT, INT, BIGINT according to width |
| unsigned integer | widened signed domain; u64 rejected | widened signed domain; u64 rejected |
| fp32 / fp64 number | float / double | FLOAT / DOUBLE |
| decimal number | decimal(p, s) | DECIMAL(p, s) |
| date | date | DATE |
| local timestamp | timestamp | TIMESTAMP(p) |
| instant/offset timestamp | timestamptz | TIMESTAMP_LTZ(p) |
| local time | time (microseconds) | TIME(p) |
| object | struct with ordered fields | ROW with ordered fields |
| array | list with element ID/type/requiredness | ARRAY with element type/nullability |
| map | explicit non-null key, key/value IDs and value nullability | MAP with explicit key/value types/nullability |

Decimals require explicit precision/scale, at most 38 digits, and valid scale. Temporal precision is limited to six fractional digits in this initial shared projection. Missing precision uses six. Microsecond Iceberg representation does not enforce a coarser source precision. Original offsets and named timezones cannot be retained; a field diagnostic reports that loss. Time-only values with instant/offset semantics, opaque objects, undeclared collection shapes, vectors, unsupported domains and precision loss fail with field context. No default or value is invented.

Native nullability is taken from canonical Nullable, independently of Required. In particular, optional/non-nullable does not become nullable. The target cannot distinguish an absent property from a value; SCHEMA_REQUIRED explains this limit. Native Iceberg required means a non-null value, and Paimon NOT NULL has the same value meaning. Descriptions use Iceberg doc and Paimon description/comment. Pattern/range/enum/default/quality constraints and relational/property policies require separate contract validation and produce diagnostics. Iceberg/Paimon do not embed a proprietary Packata constraint extension. Generic Avro/Spark retain the existing versioned residual Packata metadata and loss policy.

## Fresh field identities and metadata

IDs are identities, not hashes of mutable names. The exporter first reserves all explicitly supplied IDs across the nested schema, then walks fields in source order, depth first, assigning the lowest unused ID starting at 1 for Iceberg or 0 for Paimon. Iceberg allocates list element and map key/value IDs as well as named fields. Paimon allocates only named row fields, including row fields nested under arrays/maps; collection elements themselves have no field IDs. highestFieldId is the maximum allocated row-field ID. Supplied Paimon element IDs are rejected.

Both exporters conservatively permit field IDs through 2147483447, excluding Iceberg’s upper system-reserved range and conservatively retaining the same headroom for Paimon, and reject duplicate, negative, fractional or malformed IDs. Iceberg also rejects 0. Numeric ODCS custom-property strings are accepted as integer declarations. The same unchanged input produces identical output. Adding or reordering fields can change automatically allocated IDs: this policy does **not** make fresh IDs stable across edits.

The canonical API accepts ExtensionMetadata namespaces iceberg and paimon. ODCS hosts accept named custom properties at the asset/field scopes (see the [complete source](../examples/lakehouse/contract.odcs.yaml)):

| Scope | ODCS custom property | Meaning |
| --- | --- | --- |
| asset | packata.iceberg.schema-id | Non-negative 32-bit schema ID, default 0 |
| field / Iceberg collection element | packata.iceberg.field-id | Explicit field identity |
| asset | packata.iceberg.identifier-fields | Distinct top-level field names; default none |
| asset | packata.paimon.schema-id | Non-negative 32-bit fresh schema revision ID, default 0 |
| named field | packata.paimon.field-id | Explicit row-field identity |
| asset | packata.paimon.partition-keys | Distinct top-level field names; default none |
| asset | packata.paimon.options | Only an empty object is accepted in this initial exporter |

Unknown target-prefixed options are errors. Data Package has no native target ID/partition declaration; automatic allocation is used for its standard fields. Canonical callers can supply target namespaces independently of the source standard. No new CLI flag or compatibility profile selects spec versions or IDs.

Iceberg identifiers are opt-in; this version supports only unambiguous top-level required, non-null primitive fields excluding float/double. Canonical primary keys are not silently converted into identifiers. Iceberg identifiers **do not enforce uniqueness**.

Paimon maps canonical primary keys only when they reference distinct required, non-null top-level primitive fields. Declared partition fields must exist; for primary-key tables this exporter requires them to belong to the primary key, with at least one non-partition primary-key field. Cross-partition policies need explicit runtime configuration. It never infers a key, partition or bucket count. options is empty and timeMillis is the deterministic sentinel 0, not a fabricated creation time. Consumers must select actual storage options through supported APIs. Explicit defaults are diagnosed but not emitted because expression/engine semantics are not established.

These are **fresh schema artifacts** for offline inspection and new-table integration. Existing-table evolution needs an explicit field-ID mapping/baseline or the supported Iceberg/Paimon APIs. Never regenerate IDs to replace an existing schema or copy exported JSON over a live Paimon schema file. No catalog reads, table registration, partition-spec generation or data/snapshot operations occur.

## Compatibility profiles

Hudi reuses the Avro serializer, including null-first unions and no inferred defaults. It requires valid Avro names without normalization, excludes Hudi-managed _hoodie_ fields, Avro enum, time-only and local-timestamp logical types, precision above Spark's decimal(38) limit, and explicit defaults not yet emitted by the serializer. These are restrictions of this exporter/engine baseline, not claims about all Hudi engines. Records, arrays, string-keyed maps, ordinary scalars, date, instant timestamp-micros and decimal bytes use the existing projection. Fields rejected only by the profile receive SCHEMA_PROFILE with source path and target hudi. Configure record keys, partition paths, ordering fields and storage separately; no managed columns are injected.

Delta reuses Spark JSON, including array containsNull and map valueContainsNull. It validates case-insensitive duplicates at every nested struct and rejects names containing spaces or ,;{}() newline tab = under column mapping none. Any incoming delta.* or packata.delta.* reserved metadata on assets, schemas, fields or collection elements is rejected. No column-mapping IDs, physical names, generated/identity expressions, protocol features or CHECK constraints are invented. Local timestamps/time-only types are already outside the shared Spark export baseline. Table partitioning, properties, constraints and feature enablement require separate runtime configuration. The JSON object can be serialized into schemaString by a consumer; the export itself is not a doubly encoded string.

Unknown/incompatible profiles fail before serialization; no-profile Avro/Spark output remains unchanged. Standalone schema validity does not prove compatibility with an existing Hudi/Delta table or records. That requires an explicit table baseline and is outside this export.

## Complete artifacts and native consumption

The [source fixture](../examples/lakehouse/contract.odcs.yaml) exports complete checked-in examples with decimals, timestamps, nested records, lists and maps:

- [Iceberg JSON](../examples/lakehouse/orders.iceberg.schema.json)
- [Paimon JSON](../examples/lakehouse/orders.paimon.schema.json)
- [Hudi Avro](../examples/lakehouse/orders.hudi.avsc)
- [Delta Spark JSON](../examples/lakehouse/orders.delta.schema.json)

Java consumption uses the supported APIs below; see the [executable validation harness](../validation/schema-consumers/README.md) for pinned dependencies and assertions of IDs, descriptions, types and nullability:

```java
var iceberg = org.apache.iceberg.SchemaParser.fromJson(icebergJson);
var paimon = org.apache.paimon.schema.TableSchema.fromJson(paimonJson);
var avro = new org.apache.avro.Schema.Parser().parse(hudiAvsc);
var hudiSpark = org.apache.hudi.AvroConversionUtils.convertAvroSchemaToStructType(avro);
var deltaSpark = (org.apache.spark.sql.types.StructType)
    org.apache.spark.sql.types.DataType.fromJson(deltaSchemaJson);
org.apache.spark.sql.delta.schema.SchemaMergingUtils.checkColumnNameDuplication(
    deltaSpark, "schema export", false);
```

The harness starts no engine and creates no table. Runtime dependencies remain outside Core and Application; the product serializers use JSON construction and the shared field resolver only.
