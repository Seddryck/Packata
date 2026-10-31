---
title: Schema code generation
parent: Reference
permalink: /reference/dotnet/schema-generation/
---

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
    new(AssetId: "orders", Target: "record", Namespace: "Sales", TypeName: "Orders"));
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

Both targets use the same field-aware `Packata.Arrow.ArrowSchemaMapper` projection, also
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
