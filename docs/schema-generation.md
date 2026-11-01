---
title: Schema code generation
parent: Reference
permalink: /reference/dotnet/schema-generation/
---

For PySpark, Arrow, DataFrame and ML.NET, see [framework schema code]({{ '/reference/dotnet/framework-schema-generation/' | relative_url }}).

For ordinary C# classes and Python classes, dataclasses and Pydantic models, see the
[model generation reference]({{ '/reference/dotnet/model-generation/' | relative_url }}),
including presence policies, mapping profiles and complete generated examples.

`SchemaGenerationService` in `Packata.Application.Generation` is the shared metadata-only
workflow for CLI and editor hosts. It accepts `ContractDocument` or descriptor text with
its original base URI, `GenerationOptions`, optional `TemplateInput` text/identity/version,
and cancellation. It returns content, suggested filename/language, selected asset,
template identity/version, context and structured diagnostics. Hosts own source snapshots,
template file access, stdout and atomic writes; Application never opens dataset rows,
contacts databases, acquires connectors, or compiles/executes generated code.

```csharp
using Packata.Application.Generation;
var source = await File.ReadAllTextAsync("sales.odcs.yaml");
var result = await new SchemaGenerationService().GenerateAsync(source,
    new(AssetId: "orders", Target: "record", Language: "csharp", Namespace: "Sales", TypeName: "Orders"));
if (!result.IsSuccessful) throw new InvalidOperationException("Inspect result.Diagnostics.");
await File.WriteAllTextAsync("Orders.cs", result.Content);
```

This library example leaves overwrite policy to its caller; the CLI protects existing
files, including descriptors and templates. Generation selects exactly one built-in
target or custom template. Single eligible assets select automatically; multiple assets
require an ID. Existing JSON Schema export retains its required asset/output-format options.

## Versioned context and custom templates

Didot.Core **0.34.0** renders the same ordinary serializable `model` envelope for all
engines. `context_version` is **1**. The engine and mapping profile are independent of the
output filename/language. Discover available suffixes, profiles, targets, defaults and
limitations through `SchemaGenerationService.Catalog`; use an explicit engine to override
the suffix. Unrecognized suffixes default to Scriban. Built-in templates declare Scriban.

Context members:

| Member | Content |
| --- | --- |
| `model.contract` | `id`, `name`, `version`; connection endpoints and dataset contents are excluded |
| `model.asset` | `id`, original `name`, `generated_name`, `description` |
| `model.fields` | Source order; `name`, `generated_name`, description, logical/physical type, format, required/nullable independently |
| Each field's `parameters` | Integer width/signedness, decimal precision/scale, temporal precision/semantics/timezone from shared Core resolution |
| Each field's `constraints`, `value_domain` | Effective constraints and allowed codes; entries retain code, label, description separately; references are retained without reads |
| Each field's `children`, `items` | Recursive ordinary context objects |
| Each field's `extensions`, `mapping_diagnostics` | Retained source-specific metadata and original shared resolution findings |
| Each field's `target` | Prepared `type_expression`, `runtime_type_expression`, source literal, initialization/nullability and metadata expressions when a profile is selected |
| `model.target` | Profile, root name, namespace, deterministic imports and named record declarations |
| `model.diagnostics` | Mapping/generation findings; irrelevant CLR/DbType projection errors are informational, not a rejection of a supported target |
| `model.variables` | Explicit `--define` values, isolated from schema metadata |

For example, save this as `bar.template.cs` beside the descriptor:

```text
// {{ model.asset.name }} — context {{ model.context_version }}
{{ for field in model.fields }}// {{ field.name }}: {{ field.logical_type }}
{{ end }}// author: {{ model.variables.author }}
```

```console
packata schema generate sales.odcs.yaml --template bar.template.cs --define author=Sales
packata schema generate sales.odcs.yaml --template bar.template.cs --engine scriban --type-mapping csharp-record
```

Definitions use the first `=` as separator. `null`, booleans, invariant integers and
decimals become corresponding scalar values; remaining text stays string. Empty keys
and duplicate keys are rejected. Keys can be accessed with engine dictionary/index syntax.
Source/context redaction also applies to sensitive variable keys and credential-bearing
strings. No filesystem/network partial loader is registered. Didot rendering is synchronous;
cancellation before preparation is immediate, and cancellation during rendering discards
the result after the non-interruptible render completes.

## Target representation and enforcement

Records require C# **11+**, nullable annotations enabled and .NET **8+**. Fields use
`required` only for required presence and `?` only for nullable values/references. Nested
objects receive deterministic named records, arrays use element projections, and
`JsonPropertyName` preserves original source names. Invalid identifier characters become
underscores, leading digits gain an underscore, keywords are escaped, and collisions
receive `_2`, `_3`, etc. Target imports, factory methods and synthesized record members
are also reserved for collision handling. Namespace segments follow the same escaping.
Required initialization and nullable annotations do not enforce contract constraints or
validate all serializers.

DataTable output is a C# factory using underlying CLR column types and independent
`AllowDBNull`. It retains requiredness, constraints, value domains, decimal parameters
and the field schema in `ExtendedProperties` under `packata.*`. Unique/range/pattern/
category constraints and nested shapes are metadata, not claimed as enforced DataTable
rules. No `Nullable<T>` is passed as a DataColumn type.

Polars output compiles against **Polars.NET 1.0.1** (`Polars.CSharp`). It constructs a
`PolarsSchema` from `DataType` expressions, preserving signed/unsigned numeric widths,
declared decimal precision/scale, structs and lists. Fixed decimal requires both declared
parameters and precision at most 38, independently of CLR decimal capacity. Temporal
precision selects milliseconds (up to 3), microseconds (up to 6), or nanoseconds (up to 9);
absent precision selects nanoseconds without claiming a declared source precision.
Nanosecond calendar-range limits and per-column timezone normalization are warnings.
Instant timestamps default to UTC representation; arbitrary per-value offsets without a
declared timezone are rejected. Polars schemas do not enforce requiredness, nullability,
categories or contract quality rules. Opaque objects/maps and unsupported precision are
errors. Execution of generated Polars code requires its matching native runtime package;
generation itself and the language server require no Polars native runtime.

Categories always retain codes separately from labels; no automatic enum or label
substitution occurs. Unknown types/formats and unsupported representations fail before
rendering. Explicit mapping fallback is available and remains visible in diagnostics.
Target expressions are prepared before Didot rendering; templates arrange them instead
of reproducing source interpretation. Mapping describes representation, not row validation.

## Offline SQL DDL

Use schema metadata to generate SQL without a connection URL, credentials, connectors, or database access:

```bash
packata schema generate sales.odcs.yaml --target ddl --dialect postgresql -o schema.sql
packata schema generate sales.odcs.yaml --target ddl --language sql --dialect sqlserver --asset orders
packata schema generate datapackage.json --target ddl --dialect sqlite --constraints PrimaryKey,Required --no-descriptions
```

Bundled dialect IDs are ansi, access, db2, duckdb, firebird, mysql, singlestore, oracle, cockroachdb, cratedb, postgresql, snowflake, sqlite, sqlserver, and teradata (DubUrl 0.55.1). Drill and QuestDB are disabled for this relational DDL workflow, including direct selectors; connection/query support does not imply DDL support. The shared Application catalog removes them from CLI and editor choices. ANSI is a generic SQL reference, not a verified database engine. SQL submenu entries preselect the exact dialect; generic generation pickers request a dialect.

Capability checks inspect only selected schema metadata and enabled constraint options, before reading external relationship descriptors or rendering SQL:

| Dialect | Constraint behavior |
| --- | --- |
| CrateDB | Requested standalone UNIQUE and FOREIGN KEY constraints fail with `PROV009`; primary keys, NOT NULL and CHECK remain available. |
| SingleStore | Requested FOREIGN KEY and CHECK constraints fail with `PROV009`; generation does not assume a server setting that accepts and ignores foreign keys. |
| Snowflake | Standard-table PRIMARY KEY, UNIQUE and FOREIGN KEY declarations receive `PROV010` Information because the engine does not enforce them. NOT NULL and CHECK are enforced. |
| DuckDB | DubUrl renders foreign keys inside CREATE TABLE and creates selected parents first. Dependency cycles fail with `PROV009`; supported self references remain inline. |
| SQLite | Foreign keys remain inline in CREATE TABLE; forward table references are retained. |

Unsupported requested constraints produce errors rather than silently disappearing. Use an explicit subset such as `--constraints PrimaryKey,Required,Checks` to opt out of UNIQUE and FOREIGN KEY generation. An enabled single-column primary key already supplies uniqueness: the same field's UNIQUE annotation is omitted. UNIQUE on one member of a composite primary key is preserved because it imposes an additional restriction.

DubUrl 0.55.1 supplies the fixes for [CHECK predicates](https://github.com/Seddryck/DubUrl/issues/1760), [safe string dimensions](https://github.com/Seddryck/DubUrl/issues/1761), [MySQL key types](https://github.com/Seddryck/DubUrl/issues/1762), [DuckDB foreign keys and ordering](https://github.com/Seddryck/DubUrl/issues/1763), [redundant uniqueness](https://github.com/Seddryck/DubUrl/issues/1764), and [comment capabilities/rendering](https://github.com/Seddryck/DubUrl/issues/1765). Packata delegates SQL generation and external-table validation to its native renderer; no private SQL templates or dependency-ordering workaround remain. `PROV006` Warning identifies a missing renderer where the engine supports comments. `PROV012` Information identifies engines that cannot express comments. MySQL, SQL Server and Snowflake descriptions now use the upstream provider renderers. The catalog does not certify every schema against every server version.

Omitting --asset selects all assets with non-empty schemas. Selecting one asset never adds related tables. Foreign keys outside the selection, unresolved keys and duplicate physical columns are errors: no script is published. Unsupported checks, comments, native physical types and qualifiers produce structured warnings using the existing provisioning planner's fallback policy. Review warnings before deploying the script. Nested columns and unsupported logical types fail. The planner preserves physical identifiers, quoting, supported qualifiers, composite keys and supported constraints; it is not a migration engine. Rendering reads no records and executes no SQL.

String storage dimensions preserve explicit physical type parameters. A declared maxLength supplies a bounded storage length for SQL Server, MySQL/SingleStore and Teradata, independently of optional CHECK emission. SQL Server unrestricted logical text uses NVARCHAR(MAX); explicit varchar(max)/nvarchar(max) are supported. Key columns require bounded types, including keys on external reference tables. MySQL rejects TEXT/BLOB key columns and Teradata requires a declared VARCHAR/CHAR length. Missing or unsafe dimensions fail with actionable PROV009 errors and no script; Packata does not guess a length such as 255. Specify physicalType in ODCS, or x-packata-physicalType in Data Package fields, for example varchar(2) on both country-code endpoints.

A maximum-length CHECK is omitted when the rendered character type already enforces an equal or stricter bound. For example, SQL Server `NVARCHAR(2)` does not also emit `CHECK (LEN(column) <= 2)`. Minimum-length and other independent checks remain. Wider types, unbounded `TEXT`/`NVARCHAR(MAX)`, and SQLite/DuckDB types whose declared lengths are not enforced retain the maximum-length CHECK. Decisions use the provider's rendered storage type, including physical-type fallback, rather than the requested type name alone.

Default options match provisioning: all supported constraints and descriptions. --constraints accepts all, none, or comma-separated PrimaryKey,Required,Unique,Checks,ForeignKeys; --no-descriptions omits comments. Application callers can supply typed DdlOptions. Provisioning retains its deployment capability boundary and delegates metadata mapping/rendering to the same RelationalSchemaPlanner. Cancellation is checked before and after synchronous rendering; cancelled results are discarded before output.

DDL foreign keys resolve same-contract references such as `countries.iso_code` and `#/schema/countries/properties/iso_code`. External ODCS references such as `countries.odcs.yaml#/schema/countries/properties/iso_code` retain the descriptor and fragment instead of treating filename extensions as table names. With foreign keys enabled, generation reads the referenced local contract relative to the source descriptor's directory, validates the target schema/field and uses physical table/column names and endpoint qualifiers. It reads contract metadata only; it does not read referenced records or open database connections. Remote references are not fetched.

Referenced tables remain validation metadata unless selected for generation: an external lookup or an unselected same-contract table can appear in `REFERENCES` without receiving a `CREATE TABLE` statement in that script. DubUrl receives the selected tables plus typed ExternalTables metadata. It validates both foreign-key endpoints without creating or dropping external tables. Missing/unreadable contracts, missing tables and invalid target columns produce actionable `PROV003` errors and no partial SQL. Disabling ForeignKeys skips external contract reads. Editor generation uses the current source buffer and resolves external contracts on disk beside that document.

A field-level `primaryKey: true` already represented by the schema primary key is handled once as a table constraint. It does not produce an unsupported-CHECK `PROV004` warning, including when primary-key generation is intentionally disabled. Unsupported CHECK constraints retain their diagnostics.

The Required constraint option controls native NOT NULL generation from field nullability. Source property presence is independent: a required property may allow null, and an optional property may forbid null when present. Primary-key columns remain NOT NULL as required by the database.


## Arrow IPC schema documents

`packata schema export sales.odcs.yaml --asset orders --output-format arrow-ipc -o orders.schema.ipc`

Arrow IPC emits one encapsulated Schema message without record batches or trailing stream markers. Redirected stdout receives the exact binary bytes; interactive terminals require `--output`. Editor exports use a protected save dialog. See [Arrow schema documents]({{ '/reference/dotnet/arrow-schema-documents/' | relative_url }}) for mapping and interoperability details.

## Structured schema documents

Non-default `allowAdditionalProperties` and `enforcePropertyOrder` policies are preserved independently in schema metadata. They receive Information diagnostics because these documents do not enforce input-record extras or property order.

```bash
packata schema export sales.odcs.yaml --asset orders --output-format spark-json -o orders.spark.schema.json
packata schema export sales.odcs.yaml --asset orders --output-format avro --schema-name Orders --schema-namespace example.sales -o orders.avsc
```

These structured serializers do not invoke Didot. JSON Schema continues using the existing Draft 2020-12 converter unchanged. Application callers holding canonical metadata can use SchemaDocumentSerializer.Serialize; CLI/LSP use the shared operation for descriptor selection, cancellation and diagnostics. No dataset records are read. --asset is optional only for exactly one eligible asset; DDL alone supports whole-contract scope. Profile targets hudi and delta are not advertised or accepted until implemented.

Avro targets the Avro 1.12 schema specification. For id: signed int64/non-null, name: string/nullable and amount: decimal(12,2)/non-null, the native schema portion (Packata metadata omitted here for readability) is:

```json
{
  "type": "record",
  "name": "orders",
  "namespace": "packata.generated",
  "fields": [
    {"name": "id", "type": "long"},
    {"name": "name", "type": ["null", "string"]},
    {"name": "amount", "type": {"type": "bytes", "logicalType": "decimal", "precision": 12, "scale": 2}}
  ]
}
```

Nullable Avro values use a null-first union. Neither optionality nor nullability implies a default; no defaults are emitted. Explicit Avro default constraints are preserved as metadata and produce an implementation-gap warning because native default rendering is not implemented. Decimal bytes represent the big-endian two's-complement unscaled integer as required by Avro; this operation creates schemas only, not encoded values. Names replace non-ASCII identifier characters with underscores and prefix digit-leading names with an underscore. Field collisions after normalization fail; nested record names use the owner/field path with deterministic numeric suffixes for record-name collisions. The default namespace is packata.generated; --schema-name and --schema-namespace override Avro naming. Namespace components must be valid Avro identifiers. These options apply only to Avro; output filenames do not select formats or names.

Spark targets portable Spark 3.x StructType JSON, validated with Microsoft.Spark 2.3.1's native JSON parser. The same example is:

```json
{"type":"struct","fields":[
  {"name":"id","type":"long","nullable":false,"metadata":{}},
  {"name":"name","type":"string","nullable":true,"metadata":{}},
  {"name":"amount","type":"decimal(12,2)","nullable":false,"metadata":{}}
]}
```

Both preserve field order and nested records/structs, arrays, element nullability and explicit map value nullability. Canonical maps carry DataMap key/value definitions (ODCS map.key/map.value). Portable map keys must be explicit non-null strings; other keys fail. Spark keeps field names exactly and rejects duplicates. Opaque objects without fields and arrays without item schemas fail. Optional property presence is distinct from value nullability: both targets lack an independent presence constraint, so optional fields produce an informational format-limit diagnostic and retain their requiredness in metadata. Constraints, quality rules, physical database types, primary keys and relationships that the target cannot enforce are preserved in metadata and produce Information diagnostics. Exporter implementation gaps produce explicit Warning diagnostics; no validation is silently claimed.

Decimal precision and scale must both be explicit. Spark allows precision up to 38; Avro retains the declared decimal domain even when it exceeds CLR Decimal. Signed integer widths map to the nearest target domain; unsigned values widen when possible, with informational range-limit diagnostics. Unsigned 64-bit values fail. fp32/fp64 map to float/double; unspecified exact numbers require explicit decimal parameters. Date maps to Spark date or Avro int/date. Instant timestamps use microsecond precision (Avro long/timestamp-micros, Spark timestamp); Avro additionally supports local-timestamp-micros and time-micros. Spark local/time-only values fail under this portable baseline. More than six fractional digits fail, coarser precision emits an informational constraint diagnostic, and original offsets/named timezones produce an informational format-limit diagnostic because they cannot round-trip. Requiredness never changes nullable-union ordering.

Parser tests use Apache.Avro 1.12.0 and Microsoft.Spark 2.3.1; Spark's test-only MessagePack dependency is pinned to 3.1.12. These target parsers are test dependencies; Packata.Core contains no Spark/Avro libraries. All fatal projection errors return no content, and hosts publish only complete successful results.

Compatible string membership restrictions now export as native Avro enums. Effective canonical allowed values are used in declaration order; category labels and unresolved external references never supply symbols. Symbols must be unique, non-empty strings matching Avro identifier syntax. Nullable enums use the existing null-first union policy, and no default is inferred. Enum type names share deterministic collision handling with record names. Incompatible restrictions retain the original scalar representation.

## Constraint metadata version 1 and diagnostic policy

Metadata contains only information not expressed by the native schema. Absent annotations, empty metadata collections/objects and empty strings are omitted. Explicit constraint values remain untouched, including null, false, zero, empty strings, empty objects and empty membership arrays. An empty membership restriction still means that no values are allowed.

Avro record-level `packata` metadata, when needed, contains `version: 1` and `schema`. Field-level `packata` contains `version` and a residual `field` object. No `packata` block is emitted when nothing remains. Spark keeps its native `metadata: {}` slot when there is no Packata metadata.

Native names, nullability, compatible Avro enum symbols, exact decimal precision/scale, exact signed integer widths and floating-point formats are not repeated in metadata. Widened integer ranges, source encodings, timezone annotations and other lost semantics remain. Native record slots already express field presence; an optional source field retains `required: false` because optional property presence differs from nullability. A matching field-level required constraint is not repeated. Original field names remain only when native naming changes them, or an array/map element has no native name slot. Descriptions use native Avro `doc`; Spark and unnamed elements retain descriptions in metadata.

Schema metadata preserves primary keys, relationships and non-default field-matching policies. A matching field-level primary-key flag is omitted in favor of the schema's key list. Asset identity is omitted where it equals the Avro record name; otherwise `assetId` remains, with `assetName` only when it differs from both that identity and the native name. Spark has no native asset identity, so it retains `assetId`. An absent `assetName` is recovered from the native Avro name when `assetId` is also absent, or from `assetId` otherwise.

Membership is stored once: native Avro symbols when supported, otherwise a single enum constraint. `valueDomain.allowedValues` is not repeated. Domain catalogs retain labels, descriptions, ordering, references and catalog entries outside the allowed membership. A catalog identical to membership and without annotations is omitted. Nested record/struct fields own their metadata directly; parent metadata does not repeat a `children` tree. Array items and map keys/values retain residual annotations under `items` and `map` where no native field metadata slot exists. References are descriptive; exporting never fetches categories or reads records.

String catalogs use code-keyed `valueDomain.labels` and optional `valueDomain.descriptions` mappings. For example, the residual metadata for the native Avro symbols `STD`, `PRE`, `HON` is:

```json
{
  "valueDomain": {
    "labels": { "STD": "Standard", "PRE": "Premium", "HON": "Honorary" }
  }
}
```

These maps annotate codes; they do not declare membership or substitute labels for values. Native Avro `symbols` remains the sole membership declaration; Spark keeps its single enum constraint in metadata. Missing/empty annotations and empty maps are omitted. Code keys are case-sensitive and retain their original spelling and JSON escaping. Both formats use the same residual shape (inside Spark's JSON-string payload).

When a string catalog's values or order differ from membership, value-only `entries` preserves the catalog sequence and extra catalog values alongside the compact maps. Otherwise `entries` is omitted. Numeric, boolean, null and mixed catalogs retain typed `entries` with their annotations, because stringifying JSON property keys could conflate distinct codes such as `1` and `"1"`. Ordering flags and unresolved references remain unchanged. Descriptions are stored separately from labels without changing native enum symbols.

Spark has field metadata but no independent StructType metadata slot. StructFields with residual metadata store `metadata.packata.version = 1` and `metadata.packata.field`. The first top-level field anchors `metadata.packata.schema` exactly once, independently of whether that field needs its own residual payload. The field/schema payloads are compact JSON **strings** inside Spark metadata, preserving mixed/null constraint values without violating Spark's supported Metadata value types. Decode the JSON strings to recover typed values. For example, in Python:

```python
import json
field_details = json.loads(schema_json["fields"][0]["metadata"].get("packata", {}).get("field", "{}"))
asset_details = json.loads(schema_json["fields"][0]["metadata"]["packata"]["schema"])
constraints = field_details.get("constraints", [])
primary_key = asset_details.get("primaryKey", [])
```

This is descriptive Packata metadata, not a standard Avro/Spark validation extension. Native consumers may ignore it; transformations dropping the first Spark field also drop the asset-level anchor. Nested item/map restrictions remain in their owning field's recursive metadata even when a primitive element has no native metadata slot. Native Avro enum symbols enforce compatible string membership; numeric, mixed, or invalid-symbol sets retain their source values/type as metadata and produce `SCHEMA_ENUM_FORMAT_LIMIT` at Information severity. Spark membership is metadata-only and informational because StructType has no enum type. Empty/multiple string membership declarations are an explicitly unimplemented native-enum case and remain `SCHEMA_IMPLEMENTATION_GAP` warnings, as do uninterpreted custom constraints and Avro explicit defaults. No default or category value is inferred.

All structured-export format limits use Information, including relational constraints, source requiredness, integer-domain widening, format/physical-type annotations and temporal offset/precision limits that can still produce a safe representation. Existing fatal invalid/unsafe mappings remain errors and publish no partial artifact. Implementation-gap warnings say what the exporter does not yet implement and what is retained as metadata. CLI renders Warning diagnostics in yellow and Information in blue on color-capable terminals. Diagnostics stay on stderr; redirected stderr remains plain text, and schema JSON on stdout stays clean. Information retains success exit code 0; LSP preserves the severity/code/source path, and VS Code opens informational-only results without a warning banner.

## Avro IDL schema documents

Use `avro-idl` for readable `.avdl` files and `avro` for JSON `.avsc` files:

```console
packata schema export contract.yaml --asset orders --output-format avro-idl -o orders.avdl
packata schema export contract.yaml --asset orders --output-format avro -o orders.avsc
```

Both formats use the same Avro projection, field ordering, normalized names, namespaces,
enum symbols, physical types, logical parameters and residual Packata metadata. IDL adds
no mapping rules and reads no dataset records. `--schema-name` and `--schema-namespace`
apply to both. IDL is a schema-document format, not a generation target or profile.
The VS Code **Export Schema > Avro IDL…** command appears when advertised by the server
catalog and opens an editable `.avdl` preview for Save As.

Output uses standalone schema syntax from [Apache Avro 1.12+ IDL](https://avro.apache.org/docs/1.12.0/idl-language/),
with one main schema and supporting named declarations in the same file. No protocols,
imports or external files are required. Identifiers are escaped consistently, namespaced
references retain their identities, and explicit unions retain branch order. Decimal
uses the bytes-based `decimal(precision, scale)` notation; microsecond temporal types
retain their physical type with `@logicalType` annotations.

The current Avro projection preserves explicit defaults as Packata metadata with a
`SCHEMA_IMPLEMENTATION_GAP` warning; IDL follows that behavior and never invents native
defaults for nullable fields. Similarly, native aliases are rendered when present in the
Avro model; IDL does not independently interpret source annotations as aliases.
Other residual properties use JSON-valued IDL annotations, including `@packata(...)`.
Consumers may ignore these descriptive constraints, just as with `.avsc`.

Unrepresentable properties fail with `AVRO_IDL_PROPERTY` and no partial output.
In particular, IDL documentation comments cannot faithfully preserve empty documentation,
leading/trailing whitespace, carriage returns, comment terminators (`*/`), or lines beginning
with indentation or a comment-decoration asterisk. These are rejected instead of changing the Avro doc.
Annotation property names must follow IDL identifier syntax; arbitrary values inside
Packata metadata remain JSON and are retained.

Reference compiler validation uses **Apache avro-tools 1.12.0**:

```console
java -jar avro-tools-1.12.0.jar idl orders.avdl orders.roundtrip.avsc
```

`AvroIdlExportTests` covers projection reuse, ODCS/Data Package input, selection failures,
collisions and unsupported annotations. Set `PACKATA_AVRO_TOOLS_JAR` to the compiler JAR
and optionally `PACKATA_JAVA` to the Java executable to enable its reference-compiler
round trips. These compare parsed Avro schemas including defaults, union ordering,
logical parameters, docs, aliases and residual metadata. No Java dependency is shipped
in production or added to Packata.Core.

## Apache Arrow schemas (C# and Python)

Use explicit target and language selectors. Generation and portable document export are separate operations:

```console
packata schema generate contract.yaml --asset orders --target arrow --language csharp --type-name Orders -o Orders.cs
packata schema generate contract.yaml --asset orders --target arrow --language python --type-name Orders -o orders_arrow_schema.py
```

C# consumers reference Apache.Arrow **23.0.0** and call `Orders.Create()` to obtain an
`Apache.Arrow.Schema`. Python consumers need Python **3.10+** and PyArrow **23+**:

```python
from orders_arrow_schema import Orders
import pyarrow as pa
assert isinstance(Orders, pa.Schema)
print(Orders.field("id"))
```

For a contract with one required, non-nullable integer `id`, the complete Python output is:

```python
import pyarrow as pa

Orders = pa.schema([pa.field("id", pa.int32(), nullable=False, metadata={"packata.required": "true"})], metadata={})
```

Field order and value nullability are retained. Presence requiredness is separately stored
in field metadata as `packata.required`. Arrow schemas do not enforce field presence,
uniqueness, value domains or quality constraints; `ARROW_CONSTRAINTS` reports this limitation.
Use `--type-name` for the exported value/factory name; names are sanitized deterministically.
Python has no namespace option. Source field names and metadata strings are escaped literals.

Both targets use the same field-aware `Packata.Arrow.ArrowSchemaProjection` projection, also
available to IPC consumers. Supported types include signed/unsigned integers, floats,
boolean, UTF-8 string, explicit string/binary, date32, time32/time64, timestamps, decimal128
(up to 38 digits), decimal256 (up to 76), nested structs and lists. Decimal precision and
scale must be declared. Temporal precision selects seconds/milliseconds/microseconds/
nanoseconds; an omitted precision uses nanoseconds to retain CLR ticks. Instants default
to UTC. Arbitrary per-row offsets without a declared zone are rejected. Epoch range and
conversion precision require separate value validation (`ARROW_TEMPORAL_RANGE`).

Canonical schema/field `Extensions["arrow"]` string values become Arrow metadata. A
canonical object with `arrow.type = "map"` and exactly two children (non-nullable key,
then value) becomes an Arrow map. This is an explicit canonical adapter convention;
native ODCS/Data Package metadata remains source-specific unless mapped to that namespace.
Dictionary types are not inferred from categorical domains. Unsupported dictionary/extension
representations and `ARROW:extension:*` metadata fail instead of changing meaning; extension
registration and storage adapters are outside this generation target.

Custom templates select `--type-mapping csharp-arrow` or `python-arrow` and can render
`model.target.schema_expression` or each field's `target.field_expression` and
`target.type_expression`. Python uses public [schema](https://arrow.apache.org/docs/python/generated/pyarrow.schema.html)
and [field](https://arrow.apache.org/docs/python/generated/pyarrow.field.html) APIs; string
metadata becomes PyArrow UTF-8 bytes. Production only renders text; Python execution and
package installation occur exclusively in interoperability tests.

The editor picker discovers both `arrow/csharp` and `arrow/python` through the Application
catalog and sends the selected language with the target through LanguageServer. Unsaved
snapshots, cancellation, diagnostics, and untitled editable output retain their existing
safeguards. Common hierarchical menu infrastructure remains owned by #699.

Focused checks: `SchemaGenerationTests`, `ArrowGenerationTests` (set `PACKATA_TEST_PYTHON`
to a Python with PyArrow), CLI `SchemaGenerationCommandTests`, LanguageServer
`SchemaGenerationOperationTests`, and VS Code `generation.test.cjs`. Python interoperability
compares generated schemas, including metadata, against IPC serialized from generated C#.
