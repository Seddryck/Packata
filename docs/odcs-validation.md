---
title: ODCS validation coverage
parent: Contracts and standards
nav_order: 2
nav_exclude: false
permalink: /standards/odcs-validation/
---

# ODCS validation coverage

`packata contract validate` checks selected descriptor rules. **Success does not
certify conformance to the complete ODCS specification or its JSON Schema, and
does not certify the data, quality rules, or SLA commitments.** This reference
inventories the source behavior at Packata revision
[`2f5136f`](https://github.com/Seddryck/Packata/tree/2f5136f), the baseline for this documentation.

## Versions and authoritative sources

The supported native model and CLI validation target **ODCS v3.2.0**. The artifact
audited here is
[`schema/odcs-json-schema-v3.2.0.json` at tag `v3.2.0`](https://github.com/bitol-io/open-data-contract-standard/blob/v3.2.0/schema/odcs-json-schema-v3.2.0.json),
which declares **JSON Schema Draft 2019-09**. Packata neither embeds nor downloads
nor executes that schema during descriptor validation. It has no schema-validator
configuration or `format` assertion mode for ODCS. The Draft 2020-12 schema produced
by `schema export` describes **data records**, not ODCS descriptors.

The [complete schema assertion inventory]({{ '/standards/odcs-schema-assertions/' | relative_url }})
records every assertion, applicator and reference in the audited artifact, including
every server variant, conditional branch and nested field. Its source SHA-256 and
regeneration command identify exactly which bytes were inventoried. Read it together
with the coverage tables below: schema keywords describe an external validator's
work, while the tables identify equivalent checks actually implemented by Packata.

The [versioned specification](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/)
is authoritative when prose and the companion schema disagree. Consult its
[fundamentals](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/fundamentals/),
[schema](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/),
[references](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/references/),
[infrastructure](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/servers/),
[quality](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/data-quality/),
[SLA](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/sla/),
[team](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/team/),
[roles](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/roles/),
[context](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/context/),
[support](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/support/),
[pricing](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/pricing/),
[custom properties](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/custom/),
[authoritative definitions](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/authoritative-definitions/),
[tags](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/tags/), and
[variables](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/variables/).

The 3.2 schema's `apiVersion` enum also accepts `v3.1.0`, `v3.0.2`, `v3.0.1`,
`v3.0.0`, `v2.2.2`, `v2.2.1`, and `v2.2.0`. This is **not Packata version support**:
the loader can deserialize older shapes, but the CLI rejects an explicitly older
API version with `ODCSV003`. There is no version-selected schema or migration
validation. Legacy team arrays and `slaDefaultElement` remain readable; deprecated
quality `rule` falls back to `metric` with a mapping warning. The v3.2 additions
(maps, vectors, enum metadata, synonyms, semantic types, deprecation, context,
encoding and new servers) are tracked by [#439](https://github.com/Seddryck/Packata/issues/439)
and [#442](https://github.com/Seddryck/Packata/issues/442); closure of those issues
does not imply complete descriptor validation.

## Stages and entry points

1. **Load and selected semantic checks.** Public
   `Packata.OpenDataContract.DataContractFactory.LoadFromFile` / `LoadFromStream`
   use the internal YAML serializer. It checks YAML placement of synonyms and
   encoding, deserializes, normalizes category values, and runs selected recursive
   property/relationship checks. It ignores unmatched properties. Failures generally
   throw `YamlException`, rather than returning a collection of schema errors.
   `Serialize` does not run these checks; constructing a native model does not either.
2. **CLI descriptor checks.** `packata contract validate contract.yaml --input-format odcs`
   uses `ContractSource.LoadAsync` and `ContractValidation.Validate`, delegating to
   public `Packata.Documents.ContractDocument.Parse` and `Validate(native)` APIs.
   Parsing requires one
   mapping document, checks identity/status/kind/API version, then invokes the mapper
   but retains only `ODCS006` and `ODCS007`. Exit `0` means no error in this limited
   report; invalid descriptors exit `1`, invocation errors `2`, and I/O failures `3`.
   Without a format override, auto-detection uses the presence of `kind`.
3. **Canonical mapping.** Public `OpenDataContractMapper.Map(native)` or
   `native.ToCanonicalContract()` returns a `MappingResult`; callers must inspect
   `Diagnostics`. It normalizes declarations and reports limitations, including
   quality and SLA warnings that `contract validate` omits. Mapping is not full
   descriptor validation. Programmatically mutated models do not
   automatically repeat the loader's semantic validation.
4. **Runtime work.** Dataset evaluation needs actual readers and complete data;
   quality execution uses `packata quality run` or explicitly compiled plans,
   expectations and backend bindings; SLA evaluation needs operational observations. Loading, mapping,
   descriptor validation, inspection and record-schema export perform none of these.
   See [rule execution]({{ '/reference/dotnet/rule-execution/' | relative_url }}),
   [data access]({{ '/guides/data/' | relative_url }}) and
   [commitments]({{ '/concepts/commitments/' | relative_url }}).

Implementation evidence:
[factory](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/DataContractFactory.cs),
[serializer](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/Serialization/Yaml/DataContractSerializer.cs),
[semantic validator](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/Validation/DataContractValidator.cs),
[CLI checks](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.Cli/Services/ContractValidation.cs),
[shared public validation](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.Documents/ContractDocument.cs),
[CLI diagnostics](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.Cli/Shared/Output/CommandOutput.cs),
[mapper](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/Mapping/OpenDataContractMapper.cs).

## Reading the matrix

All rows target **ODCS 3.2.0**. Paths starting `$defs/` are schema definition paths;
the [assertion inventory]({{ '/standards/odcs-schema-assertions/' | relative_url }})
expands their precise keyword locations. **JSON Schema: yes** means the official
artifact expresses the descriptor check, never that Packata runs it. **Partial**
means only part of the invariant is expressed. **Semantic** means additional
descriptor checks; **runtime** requires external observations. **Not validated**
identifies a gap, not a successful check. Implemented statuses are limited to the
stated layer and scope; partial is incomplete coverage, unsupported is absent,
and planned links an open delivery issue without promising an implementation.

### Descriptor structure, types and schema elements

| Section / invariant | Origin | Official JSON Schema | Other layer and current Packata status | Evidence / gap |
| --- | --- | --- | --- | --- |
| One mapping document; YAML syntax and native value conversion | Packata input contract | Schema only sees the converted JSON instance | Semantic: implemented in CLI; native YAML parsing is not strict JSON type validation | `ContractSource.Parse`; [CLI tests][cli-tests] |
| Required root `version`, `apiVersion`, `kind`, `id`; types and allowed kind/version | ODCS | Yes: root `required`, `properties/*/type`, `kind/enum`, `apiVersion/enum` | Partial: CLI requires nonblank id/version and exact kind/3.2; native defaults fill absent kind/API version before CLI sees them | [CLI tests][cli-tests]; descriptor gap below |
| Nonblank `status` | Packata CLI requirement | No root requirement; `status` only has `type: string`, examples are not an enum | Semantic: implemented by CLI (`ODCSV001`); library loading alone does not require it | [CLI tests][cli-tests] |
| UUID identity, semantic-version syntax, fixed status vocabulary | Recommended conventions unless required by another consumer | No UUID format, version pattern or status enum | Not validated: unsupported as strict rules; do not infer these from examples | Root inventory; descriptor gap |
| Root and nested allowed fields, required members, types, enums, null handling and closure | ODCS companion schema | Yes where declared: `type`, `required`, `enum`, `additionalProperties`, `unevaluatedProperties`; exact coverage in inventory | Partial native type conversion; unmatched YAML fields are ignored. Schema closure and all required fields are not enforced | Serializer; descriptor gap |
| `contractCreatedTs`, member dateIn/dateOut, URI formats | ODCS | `format: date-time`, `date`, `uri` where inventoried; Draft 2019-09 validators must enable format assertions to enforce them | Not validated comprehensively; `contractCreatedTs` has no native model property | `DataContract`; descriptor gap |
| Schema object/property names; object `logicalType: object`; recursive properties, array items and map members | ODCS | Yes: `$defs/SchemaObject`, `SchemaProperty`, `SchemaItemProperty`, `SchemaBaseProperty`; `required`, `allOf`, `$ref` | Partial: typed native structures and recursive selected checks; C# `required` is not proof that YAML presence was validated | [v3.2 tests][v32-tests]; descriptor gap |
| Property logical-type vocabulary and conditional logicalTypeOptions | ODCS | Yes: `$defs/SchemaBaseProperty/properties/logicalType/enum` and `allOf/0..9` | Partial: known types resolved, absent becomes unspecified, unknown becomes unknown; aliases `datetime`/`decimal` exceed the official enum | `SchemaBaseProperty`; [temporal tests][temporal-tests]; descriptor gap |
| String format/pattern/length; numeric bounds/multipleOf/format; object required names/counts; array counts/uniqueness | ODCS | Partial: conditional option declaration types, enums and nonnegative sizes; declaring a bound does not check data values or generally establish min ≤ max | Partial metadata parsing/mapping; full descriptor consistency unsupported. Runtime conformance is separate | LogicalTypes; [constraint delivery #597](https://github.com/Seddryck/Packata/issues/597) |
| Decimal precision/scale integers, precision > 0, scale ≥ 0 and scale ≤ precision; reject them on nondecimal types | Packata extension | No corresponding precision/scale assertions in this artifact | Semantic: implemented for resolved numeric types and selected nondecimal types; not a blanket check for every type | `NumberLogicalType`, `SchemaBaseProperty`; descriptor gap for wider coverage |
| `logicalType: map` requires `map.key` and `map.value` | ODCS | Yes: `$defs/SchemaBaseProperty/allOf/8/then/required` and `properties/map/required` | Partial: map models and member recursion exist; no explicit validator requiring the map block when logicalType is map | [v3.2 serialization tests][v32-serialization]; descriptor gap |
| Vector dimensions: required positive integer; elementType/distanceMetric enums, normalized boolean and model strings | ODCS | Partial: `allOf/9/then/properties/logicalTypeOptions` requires dimensions **if options exists**, but the branch does not require logicalTypeOptions itself | Semantic: dimensions implemented even when options absent; optional vector enums are not checked by native validator | [v3.2 tests][v32-tests]; descriptor gap |
| Enum nonempty, each entry has scalar value, unique values compatible with logicalType | ODCS | Partial: `enum/minItems`, `$defs/EnumValue/required`, scalar `$ref`; no projection-based value uniqueness or parent-type compatibility check | Semantic: implemented with CLR-type/value duplicate keys and invariant parsing for compatibility, not a general schema equality engine; quoted numeric text can pass numeric compatibility | Validator; `EnumerationValueConverter`; [v3.2 tests][v32-tests] |
| Enum metadata, synonyms metadata, deprecation boolean, semanticType vocabulary | ODCS | Yes where declared: `$defs/EnumValue`, `Synonym`, SchemaElement and SchemaBaseProperty | Partial: semanticType column/measure/dimension and nonblank synonym implemented; default column and false deprecation applied. Remaining metadata vocabulary/ID rules not comprehensively checked | [v3.2 tests][v32-tests]; descriptor gap |
| Synonyms only on schema objects/properties; encoding only on allowed servers | ODCS | Partial: allowed properties and closure at specific locations; permissive nested objects are not a universal placement prohibition | Semantic: implemented recursive YAML placement scan; it also scans custom metadata, so these names there can be rejected | `ValidateYamlStructure`; [v3.2 tests][v32-tests] |

### Keys, references and cross-object consistency

| Section / invariant | Origin | Official JSON Schema | Other layer and current Packata status | Evidence / gap |
| --- | --- | --- | --- | --- |
| Stable ID character exclusions | ODCS | Yes: `$defs/StableId/pattern` wherever referenced; root id is just string | Semantic: partial, relationship IDs only; accepts namespaced colon IDs. Schema/property/enum/synonym IDs do not gain the same check | [relationship tests][relationship-tests]; descriptor gap |
| Stable IDs unique in scope; schema/property/server names unambiguous | ODCS reference semantics | No general ID/name projection uniqueness check | Semantic: partial, relationship ID uniqueness within each owner collection only; cross-object uniqueness unsupported | [relationship tests][relationship-tests]; descriptor gap |
| Relationship `type: foreignKey`; schema-level from/to, property-level to and forbidden from; scalar/array paired shape | ODCS | Yes: `$defs/RelationshipBase`, `RelationshipSchemaLevel`, `RelationshipPropertyLevel`, `oneOf`, `not`, `required` | Partial: native model and mapper support selected single-field paths; no comprehensive enforcement of these shapes | Mapper; descriptor gap |
| Reference notation and stable paths | ODCS | Partial: `$defs/ShorthandReference`, `FullyQualifiedReference` patterns check syntax, not existence | Partial mapper splits selected paths and warns `ODCS003`; it is not a complete ID resolver, and uses schema/property names as canonical identities | Mapper; descriptor gap |
| Composite from/to arrays equal length; target object/field exists; compatible referenced types; external contract resolution | ODCS reference semantics | No equal-length comparison or target resolution; schema only constrains paired array/scalar shape and item syntax | Semantic: unsupported as comprehensive descriptor validation; no external reference fetch. Runtime referential integrity also needs actual rows | Descriptor gap; runtime #597 |
| primaryKey/primaryKeyPosition, partitioned/partitionKeyPosition coherence, uniqueness/contiguous order | ODCS declarations; coherence checks depend on policy | Partial: property types, not full dependency/order checks | Partial: primary fields sorted by position during mapping; no full semantic consistency check. Uniqueness/non-null data requires runtime | Mapper; descriptor gap; #597 |
| Example values conform to declared type/options; transformations and encryptedName targets are meaningful | ODCS metadata; verifying examples is recommended | No data conformance or transformation execution | Not validated: unsupported; examples and transformation text are metadata | Schema inventory; descriptor gap |

### Servers and runtime bindings

| Section / invariant | Origin | Official JSON Schema | Other layer and current Packata status | Evidence / gap |
| --- | --- | --- | --- | --- |
| Server name/type, all type-specific required fields and options, port shape, URI/pattern constraints | ODCS | Yes: `$defs/Server/required`, `type/enum`, `allOf/0..43` and every `$defs/ServerSource/*` definition; `$defs/Port` | Partial: supported discriminators/models do not implement all conditional required/format checks; no port-range rule should be inferred where absent | [server discriminator](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/Serialization/Yaml/ServerTypeDiscriminator.cs); descriptor gap |
| All schema server kinds are usable Packata endpoints | ODCS declares kinds; executable support is Packata-specific | No: schema acceptance says nothing about available readers/drivers | Unsupported as a universal claim: official kinds include bigquery, cloudsql, hive, impala, postgres, pubsub, redshift, synapse which the native discriminator does not register | Discriminator; [reader guide]({{ '/guides/readers-storage/' | relative_url }}) |
| v3.2 hana/iceberg/exasol/teradata/ingres/vectorwise/versant/poet/zen and fastobjects/btrieve aliases; optional Athena stagingDir/workgroup | ODCS | Yes: corresponding conditional definitions; Athena still requires schema | Partial: variants/aliases round-trip, but native model shape does not establish every required field (Athena has no Schema property) | [v3.2 serialization tests][v32-serialization]; descriptor gap |
| encoding placement and extension-friendly text | ODCS | Partial: allowed server definitions contain string encoding; no fixed enum | Semantic: implemented for azure/glue/custom/kafka/kinesis/local/s3/sftp; native default UTF-8, arbitrary text preserved | [v3.2 tests][v32-tests] |
| Multiple servers map unambiguously; local directory server needs per-object physicalName | Packata binding rules | No equivalent constraints | Semantic: implemented mapping warning `ODCS001`; missing physicalName error `ODCS006`, retained by CLI validation | Mapper; [category CLI tests][category-cli-tests] |
| Endpoint exists, credentials work, database/storage schema matches, encoding is usable, reader installed | Runtime prerequisites | No | Runtime: only checked when an appropriate acquisition/reader/schema operation runs; descriptor validation does not connect or read data | [database guide]({{ '/database-integration/' | relative_url }}); reader guide |

### Quality and service commitments

| Section / invariant | Origin | Official JSON Schema | Other layer and current Packata status | Evidence / gap |
| --- | --- | --- | --- | --- |
| Quality type/dimension and metadata shapes; Library metric enum/arguments, SQL query, custom engine/implementation | ODCS | Yes: `$defs/DataQuality`, `DataQualityChecks`, `DataQualityLibrary`, `DataQualitySql`, `DataQualityCustom`; conditional required fields | Partial: native parsing and canonical mapping; missing metric/query and malformed custom payloads produce `ODCS_QUALITY` warnings, omitted by CLI validation | [QualityMapper](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/Mapping/QualityMapper.cs); [quality tests][quality-tests]; #592 |
| Expectations, range cardinality, simultaneous operators, deprecated rule/metric conflicts | ODCS plus Packata conjunction/precedence policy | Partial: `$defs/DataQualityOperators/oneOf` requires exactly one operator branch; ranges minItems/maxItems 2 | Semantic: mapper diagnoses invalid ranges, metric wins over rule; execution supports conjunctive comparisons, which can exceed schema acceptance. CLI success is not semantic approval of quality | [quality tests][quality-tests]; [execution reference]({{ '/reference/dotnet/rule-execution/' | relative_url }}) |
| Execute Library, SQL or explicit Expressif custom quality and assertions | ODCS declarations; Packata adapters/payload | No: descriptor query/program text is not execution | Runtime: implemented for supported catalog/programs through compilers/RuleRunner and `quality run` with data scope, complete input, bindings, limits and observation time. Text rules skipped; unregistered engines/configurations can error; no automatic execution from validate | [CLI quality execution]({{ '/cli-quality/' | relative_url }}); execution reference; [#591](https://github.com/Seddryck/Packata/issues/591), [delivered report #658](https://github.com/Seddryck/Packata/issues/658) |
| SLA property/value/unit/element/extensions shapes | ODCS | Yes: `$defs/ServiceLevelAgreementProperty` and referenced definitions | Partial: native retention and selected normalization; unsupported declarations produce `ODCS004`, malformed supported ones `ODCS005`, omitted by CLI validate | [SLA mapper](https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract/Mapping/ServiceCommitmentMapper.cs); [SLA tests][sla-tests] |
| SLA units/durations/scopes, explicit zones, availability 0..100, compatible zones and lifecycle milestones | ODCS declarations; Packata normalization policy | No comprehensive domain relationships (value is broadly typed); JSON shape is insufficient | Semantic: partial, supported normalized commitments enforce units, nonnegative/representable durations, calendar months, explicit ISO zones, supported element paths and duplicate lifecycle dates; unsupported bounds/scopes remain extensions with warnings | SLA mapper/tests; [commitment reference]({{ '/concepts/commitments/' | relative_url }}) |
| Observe freshness, latency, availability, retention or lifecycle compliance; execute schedules | Operational commitments | No | Runtime: SLA observation bindings/evaluation planned in [#598](https://github.com/Seddryck/Packata/issues/598); no scheduler/monitor activated by parsing. Lifecycle dates remain declarations | #591/#598 |
| Actual requiredness, types, nullability, bounds, enum membership, uniqueness and foreign keys in rows | ODCS schema declarations | No: the descriptor schema validates declarations, not datasets | Runtime: general automatic ODCS constraint conformance is unsupported; shared constraint compilation planned #597. Explicit quality rules can evaluate supported metrics, without certifying every schema declaration. Needs correct asset/endpoint, complete rows and category lookup data when referenced | [value domains]({{ '/value-domains/' | relative_url }}); #597 |

### Governance, context and extensions

| Section / invariant | Origin | Official JSON Schema | Other layer and current Packata status | Evidence / gap |
| --- | --- | --- | --- | --- |
| Team object/legacy array, members/username, date fields, roles/role, support/channel, price fields | ODCS | Yes: root team oneOf, `$defs/Team`, `TeamMember`, `Role`, `SupportItem`, `Pricing`; required/type/format where listed | Partial native models/converters; no comprehensive descriptor enforcement. Team dates ordered, users exist, role access works, support reachable and pricing applies are not checked | Serialization Team/Role/Support/Price tests; descriptor gap |
| Context shorthand or structured instructions; verifiedStatements question, constraints constraint, nested metadata | ODCS | Yes: `$defs/Context/oneOf`, nested required/type rules | Partial native conversion/retention; no proof that a verified answer is correct or a natural-language instruction is satisfiable | ContextSerializationTests; descriptor gap |
| Authoritative definitions url/type, tags, custom property property/value and nested metadata | ODCS | Yes: `$defs/AuthoritativeDefinitions`, `Tags`, `CustomProperties`, `CustomProperty`, `AnyType` | Partial native retention; URL ownership/reachability/authority and business meaning not validated; arbitrary extensions are not automatically interpreted | Native serialization tests; descriptor gap |
| Packata categoryFieldMap/categories vendor extension: valid shapes, unique declarations, table/column targets, types, required/unique value fields, label encoding, inline-enum conflict | Packata extension | No domain semantics in generic customProperties | Semantic: implemented mapper errors `ODCS007`, included in CLI validate. Lookup rows not loaded here | [category mapping tests][category-tests]; [category guide]({{ '/guides/data/' | relative_url }}#referenced-category-tables-in-odcs) |
| Variables `${NAME}` / `${NAME:-default}` | ODCS convention | No general substitution/resolution guarantee | Semantic: tokens preserved; explicit `OdcsVariableResolver.Resolve` resolves caller-supplied values/defaults, throws for missing values. No implicit environment lookup or resolution during loading | V32SerializationTests; OdcsVariableResolver |
| Business policy (approved owners, classification vocabulary, encryption, access governance, regulatory compliance) | Organization-specific, not generic ODCS assertions | Not applicable without a separately defined policy | Not validated: unsupported as generic certification; a descriptor or successful rule is insufficient evidence | Define policy and external observations explicitly |

## Schema gaps versus semantic requirements

Several checks could be expressed by JSON Schema but are **not enforced by this
artifact**: requiring nonblank root strings, requiring status, enum value uniqueness
by `value` rather than whole-object equality, and requiring logicalTypeOptions for
a vector. Do not describe these as schema checks simply because another schema
could add them. A URI `format` cannot establish reachability; `default` cannot
establish presence; `uniqueItems` cannot compare a selected ID across objects.
General target resolution, parent-dependent type compatibility, matching composite
key lengths and observed SLA compliance require additional logic.

Remaining descriptor gaps in the tables are tracked by the **descriptor gap issue**
linked below. Its scope includes strict schema evaluation and source-presence
diagnostics, ID/reference/key consistency, server/metadata coverage, and regression
cases for differences between the specification, schema and native models. This
documentation does not implement those checks. Runtime delivery is separately
tracked by #597 and #598; delivered quality execution (#658) is documented in the
execution reference. A recommendation without a normative ODCS requirement must
remain an explicit policy, rather than silently becoming an ODCS error.

The descriptor follow-up is [#665: Enforce complete ODCS descriptor validation and semantic consistency](https://github.com/Seddryck/Packata/issues/665).
Every table entry marked "descriptor gap" refers to this issue, including the
equivalent checks absent from the assertion inventory's JSON Schema layer.

### Specification and schema disagreements

The specification's field tables and prose can impose rules absent from the schema,
or allow shapes that the schema rejects. These cases need an explicit policy in
#665; none is resolved merely by running an external schema validator.

| Versioned specification rule | Actual companion schema | Current Packata status |
| --- | --- | --- |
| [Schema section is required](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/#schema-top-level) | Root `required` omits `schema`; absence and empty arrays pass | Unsupported presence check: omitted schema becomes an empty native collection and passes CLI validation when identity/status are present |
| [Element logicalType is optional; objects may be arrays](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/#applicable-to-elements-either-objects-or-properties) | `SchemaObject/required` requires logicalType and its enum permits only object | Partial: native objects default to object; properties may have unspecified logicalType. No strict object logicalType validation |
| [Null can illustrate an omitted optional field](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/#notes) | Fields with `type: string` generally reject explicit null | Partial: native nullable values are not proof of schema acceptance; compare original descriptor before normalization |
| [Vector dimensions are always required](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/#vectors) | Conditional `required: [dimensions]` only applies inside an existing options block | Implemented semantic dimension check, stricter than this schema branch |
| [Enum entries must be unique and compatible with the property](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/#enumerations) | `minItems` and scalar shapes, without parent-type/value uniqueness logic | Implemented selected native compatibility and duplicate checks, with the limitations stated above |
| [Composite reference arrays have equal lengths](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/references/#important-rules) | Paired shape checks do not compare lengths | Unsupported descriptor consistency check; comparing declarations needs no data access even though ODCS calls this runtime implementation validation |
| [Property required describes null permission](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/schema/#applicable-to-properties) | `required` is boolean metadata; `nullable` is absent from SchemaBaseProperty | Partial: mapper copies native `required` to canonical requiredness and separately sets canonical nullability from the Packata `nullable` property (default false). This distinction is Packata behavior, not proof of ODCS null semantics; descriptor validation does not examine rows |

Temporal format strings follow the specification's JDK DateTimeFormatter convention;
Packata retains format metadata but its native bound parsing uses .NET types and
selected converters. It does not validate every JDK pattern, timezone identifier,
bound ordering or ECMA-262 pattern during descriptor loading. These are partial
semantic coverage, separate from testing actual values. See
[temporal tests][temporal-tests] and the logical-type implementation.

The inventory is generated from the tagged schema and guards its source fingerprint;
an upstream byte change requires an explicit review. It includes conditional
branches and schema definitions that Packata cannot currently deserialize. Schema
acceptance and native-model acceptance therefore must be reported independently.

## Reproducible examples and diagnostics

Download [valid.yaml]({{ '/samples/odcs-validation/valid.yaml' | relative_url }}),
[invalid-enum.yaml]({{ '/samples/odcs-validation/invalid-enum.yaml' | relative_url }}),
[missing-status.yaml]({{ '/samples/odcs-validation/missing-status.yaml' | relative_url }}),
[unknown-field.yaml]({{ '/samples/odcs-validation/unknown-field.yaml' | relative_url }}), and
[older-version.yaml]({{ '/samples/odcs-validation/older-version.yaml' | relative_url }}).
Run each with an explicitly selected source standard:

```console
packata contract validate valid.yaml --input-format odcs --output-format json
packata contract validate invalid-enum.yaml --input-format odcs --output-format json
packata contract validate missing-status.yaml --input-format odcs --output-format json
packata contract validate unknown-field.yaml --input-format odcs --output-format json
packata contract validate older-version.yaml --input-format odcs --output-format json
```

| Sample | Official 3.2 schema | Expected current Packata CLI result |
| --- | --- | --- |
| valid | Pass | Exit 0, valid true, no diagnostics; does not open data |
| invalid-enum (empty property enum) | Fail: SchemaBaseProperty enum minItems | Exit 1, valid false, redacted `CLI001`; library exception contains `schema/orders/properties/status` and `at least one value` |
| missing-status | Pass: status is optional | Exit 1, `ODCSV001`, sourcePath `$.status`, `A non-empty value is required.` |
| unknown-field | Fail: root additionalProperties/unevaluatedProperties | Exit 0, valid true: unmatched field ignored; demonstrates incomplete coverage |
| older-version (v3.1.0) | Pass: apiVersion enum accepts it | Exit 1, `ODCSV003`, sourcePath `$.apiVersion`, `Supported ODCS apiVersion is v3.2.0.` |

The [missing-schema sample]({{ '/samples/odcs-validation/missing-schema.yaml' | relative_url }})
passes both the companion schema and current CLI, despite the specification's
required schema section. This demonstrates a specification gap as well as an
implementation gap. The [missing-vector-options sample]({{ '/samples/odcs-validation/missing-vector-options.yaml' | relative_url }})
passes the companion schema but exits 1 with `CLI001` in Packata: the loader requires
positive vector dimensions. Run either with the same command options as above.

Repository maintainers can replay all seven CLI examples after building the CLI:

```console
node .github/scripts/Test-OdcsValidationExamples.mjs
```

For a public-library example, loading valid.yaml succeeds; loading invalid-enum.yaml
throws. There is no JSON Schema validation implied by this call:

```csharp
using Packata.OpenDataContract;
using Packata.OpenDataContract.Mapping;

var native = new DataContractFactory().LoadFromFile("valid.yaml");
var mapped = native.ToCanonicalContract();
foreach (var diagnostic in mapped.Diagnostics)
    Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.SourcePath}: {diagnostic.Message}");
```

Library exceptions can contain source-specific paths/messages. The CLI intentionally
redacts parser exceptions to `CLI001` rather than printing the detailed library
exception. Named CLI checks retain their structured codes/paths. Neither interface
promises all errors in one pass.

[cli-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.Cli.Testing/ValidateCommandTests.cs
[category-cli-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.Cli.Testing/OdcsCategoryTableTests.cs
[v32-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Serialization/Yaml/V32ValidationTests.cs
[v32-serialization]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Serialization/Yaml/V32SerializationTests.cs
[temporal-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Serialization/Yaml/TemporalLogicalTypeTests.cs
[relationship-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Serialization/Yaml/RelationshipSerializationTests.cs
[quality-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Mapping/QualityMappingTests.cs
[sla-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Mapping/ServiceCommitmentMapperTests.cs
[category-tests]: https://github.com/Seddryck/Packata/blob/2f5136f/src/Packata.OpenDataContract.Testing/Mapping/CategoryTableMappingTests.cs
