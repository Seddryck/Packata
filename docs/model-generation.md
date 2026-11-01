---
title: C# and Python models
parent: Reference
permalink: /reference/dotnet/model-generation/
---

Generate native models from descriptor metadata through the shared Application service,
CLI or LanguageServer. Generation returns text only; it never executes generated code,
reads dataset rows, creates an ORM model or provides a model-validation service.

```console
packata schema generate contract.yaml --asset orders --target class --language csharp -o Order.cs
packata schema generate contract.yaml --asset orders --target class --language python -o order.py
packata schema generate contract.yaml --asset orders --target dataclass --language python -o order_dataclass.py
packata schema generate contract.yaml --asset orders --target pydantic --language python -o order_model.py
```

Both selectors are required for bundled models. Extensions never choose the target or
language. `record/csharp` remains a distinct record target; no shortcut flags exist.
Unsupported pairs report supported alternatives. Asset selection is optional only with
one eligible asset. Existing files, descriptor files and templates are protected; CLI
file creation is atomic. Export selectors and SQL options do not apply to these models.

The editor exposes **Generate Schema Code > C# > Class…** and **Python > Class… /
Dataclass… / Pydantic Model…** only when advertised by its server. Commands preserve
the clicked asset and unsaved source snapshot, offer applicable name options, and open
an untitled text preview for explicit save. Python has no namespace option.

## Versions, initialization and presence

| Target/language | Requirements | Required field | Optional field |
| --- | --- | --- | --- |
| `class/csharp` | C# 11, .NET 8+ | `required T` property; nullable values use `T?` | `OptionalValue<T>`, with a separate `HasValue` flag |
| `class/python` | Python 3.10+, standard library | Required keyword-only constructor argument | `T \| Unset = UNSET` |
| `dataclass/python` | Python 3.10+, standard library | Required keyword-only dataclass field | `T \| Unset = UNSET` |
| `pydantic/python` | Python 3.10+, Pydantic 2 | `Field(...)` semantics without a default, even for `T \| None` | Nullable fields use `None` storage; inspect `model_fields_set` for presence |

Executable fixtures were checked with Python 3.12.14 and Pydantic 2.14.1; C# compilation
fixtures explicitly select language version 11. Production generation has no Python
runtime dependency and adds no model-framework dependency to Core.

C# required members and nullable annotations guide compilation; they do not validate
dataset values. Ordinary Python annotations and dataclasses do not perform runtime
type, nullability or constraint validation either. Callers must validate separately.
The Python class constructor assigns arguments directly; nested dictionaries are not
automatically converted into nested plain classes or dataclasses.

Absence is independent of null. A default C# `OptionalValue<T>` has `HasValue == false`;
reading its `Value` throws. `new OptionalValue<string?>(null)` is present and null.
Use `UNSET` identity in Python; nullable optional values can independently be `UNSET`,
`None`, or a value. Sentinel defaults are absence markers, not dataset defaults.
Required-but-nullable fields still require explicit initialization.

Pydantic optional nullable fields store `None` when omitted, but omission is represented
by absence from `model_fields_set`; `model_dump(exclude_unset=True, by_alias=True)`
preserves that distinction. Optional **non-nullable** fields produce a field-specific
`MODEL_PRESENCE` error: this profile cannot represent their absence without a default.
Declared defaults are diagnosed and not applied; no scalar, empty list, map or nested
object default is invented. Consequently no mutable defaults are shared and there is
no legitimate emitted mutable default requiring `default_factory`.

Pydantic uses `ConfigDict(strict=True, populate_by_name=True)`: Python inputs are not
coerced (for example strings do not become integers, and decimal values use `Decimal`).
Pydantic's JSON entry points retain their documented JSON-specific conversions.
Nested Pydantic input dictionaries become nested models. Strict configuration does not
replace dataset validation, and arbitrary post-construction assignments are not validated.

## Mappings, names and validation limits

| Canonical type | C# | Python |
| --- | --- | --- |
| string, boolean | `string`, `bool` | `str`, `bool` |
| integer | Field-resolved signed/unsigned CLR integer | Arbitrary-precision `int`; Pydantic enforces explicit integer width bounds |
| number | `decimal`, or `float`/`double` for `fp32`/`fp64` | `Decimal`, or `float` for `fp32`/`fp64` |
| date, time | `DateOnly`, `TimeOnly` | `date`, `time` |
| datetime, timestamp | Resolved `DateTime` / `DateTimeOffset` | `datetime` |
| year, yearmonth | `int`, `string` | `int`, `str` |
| object with children | Deterministic generated class | Deterministic generated model of the selected style |
| opaque object | `object` | `dict[str, object]` |
| array with explicit items | Item type array | `list[item type]` |
| explicit map | `Dictionary<string, value type>` | `dict[str, value type]` |

Map keys must be non-nullable strings. Unsupported shapes, unknown logical types and
lossy conversions produce field-specific diagnostics; generation emits no partial code
after an error. C# decimal domains beyond 28 guaranteed digits/scale are rejected.
Python decimals do not inherit CLR capacity limits; Python temporal precision beyond
six fractional digits is rejected. Timezone/offset semantics remain metadata for Python
and generate a warning. Floating formats do not claim exact fixed decimal semantics.
The strict model profiles reject unrepresentable types even when a general mapping
fallback was requested; fallbacks do not authorize silent model type changes.

Names are sanitized, reserved words escaped and collisions suffixed deterministically.
Python uses ASCII identifiers and renames leading underscores, reserved imports and
Pydantic `model_` members. Pydantic additionally reserves other fields' original names
so a generated identifier cannot capture another field's input alias. Original field
names and descriptions remain in canonical context. Pydantic emits original-name aliases
and descriptions, and C# emits `JsonPropertyName` attributes. Plain Python models use
generated identifiers; they have no serializer or alias ingestion layer. C# optional
wrappers are initialization containers; consumers need their own serializer/converter
policy to serialize wrapper values or omit absent members.

Pydantic enforces numeric inclusive/exclusive bounds, string/list/map length limits,
decimal precision/scale, explicit integer domains, and nonempty string/integer/boolean
allowed-value domains via `Literal`. Decimal bounds are emitted without float rounding.
Constraints on array items and map values apply through `Annotated[..., Field(...)]`.
Descriptions do not become validation. Patterns, uniqueness, explicit defaults,
referenced value domains, string formats, temporal semantics and arbitrary quality rules
are diagnosed as unenforced. Keys, relationships, additional-property and property-order
policies require separate validation. Plain models warn when constraints/domain/default
semantics are not enforced. All original constraints remain available to custom templates.

## Application and custom templates

```csharp
using Packata.Application.Generation;
var result = await new SchemaGenerationService().GenerateAsync(source,
    new(Target: "dataclass", Language: "python", TypeName: "Order"),
    cancellationToken: cancellationToken);
// Check result.IsSuccessful and result.Diagnostics before using result.Content.
```

Stable context version 1 profiles are `csharp-class`, `python-class`, `python-dataclass`
and `python-pydantic`. Select one with `--type-mapping` in custom-template mode:

```console
packata schema generate contract.yaml --template model.scriban --type-mapping python-dataclass
```

The canonical fields retain independent `required`, `nullable`, `source_path`, original
names, parameters, constraints, domains, `children`, `items`, `map_key` and `map_value`.
`model.target.declarations` is in dependency order with the root last. Each declaration
has `name` and `fields`; field `generated_name` and `target.type_expression` are ready
for the chosen model style. `target.value_type_expression` excludes the outer absence
wrapper/sentinel; `required_keyword`, `initializer`, `source_name_literal`,
`description_literal` and Pydantic `field_arguments` are also prepared. Literals use the
profile language. The shared service owns selection, diagnostics and cancellation;
custom templates do not start an independent host pipeline or access datasets.

## Complete examples

The checked-in [source contract](https://github.com/Seddryck/Packata/blob/main/examples/schema-models/contract.odcs.yaml)
has required `id`, required nullable `label`, and optional nullable `note`. Complete
generated files are [C# class](https://github.com/Seddryck/Packata/blob/main/examples/schema-models/Order.cs),
[Python class](https://github.com/Seddryck/Packata/blob/main/examples/schema-models/order.py),
[dataclass](https://github.com/Seddryck/Packata/blob/main/examples/schema-models/order_dataclass.py)
and [Pydantic](https://github.com/Seddryck/Packata/blob/main/examples/schema-models/order_model.py).
Reproduce them with the four commands above, using this source and `--type-name Order`.

```python
# Ordinary class or dataclass:
from order import Order, UNSET
missing = Order(id=1, label=None)
assert missing.note is UNSET
assert Order(id=1, label=None, note=None).note is None

# Pydantic:
from order_model import Order as OrderModel
assert 'note' not in OrderModel(id=1, label=None).model_fields_set
assert 'note' in OrderModel(id=1, label=None, note=None).model_fields_set
```

See the upstream [Pydantic field/default documentation](https://docs.pydantic.dev/latest/concepts/fields/)
and [Python dataclass documentation](https://docs.python.org/3/library/dataclasses.html)
for constructor and validation behavior.
