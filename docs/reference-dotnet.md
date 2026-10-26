---
title: .NET library
parent: Reference
nav_order: 2
nav_exclude: false
permalink: /reference/dotnet/
---

The libraries target .NET 8, 9 and 10. Start with the
[runnable Quickstart]({{ '/quickstart/dotnet/' | relative_url }}), then add the packages your
application needs. The format-specific packages already reference Core.

The table below identifies the main entry points, and the source links later on this page
provide their signatures. Exhaustive generated API documentation is deferred.

| Package | Purpose / public entry points |
| --- | --- |
| `Packata.Core` | Canonical contracts, `DataContract`, mapping/inference results, diagnostics, typed differences, `IDataEndpointReaderFactory`, `IDataContractProvisioner` |
| `Packata.DataPackage` | Native v2 models, `DataPackageFactory`, JSON/YAML serializers and validators, `ToCanonicalContract`, `CsvDialectExporter` |
| `Packata.OpenDataContract` | Native ODCS models, `DataContractFactory`, YAML serializer/validation, `ToCanonicalContract` |
| `Packata.ResourceReaders` | `ResourceReaderFactory`, `CsvDataContractInference`, formats, custom providers and delimited dialect resolution |
| Reader extensions | [Format/package/registration table]({{ '/guides/readers-storage/' | relative_url }}); database extension also exposes `DatabaseDataContractInference` |
| `Packata.Storages` | `DataPackageLocatorBuilder`, `StorageProviderBuilder`, document containers, local/HTTP(S)/ZIP/TAR/cloud storage; `UseTarArchive`, `ArchiveCompression`, `TarArchiveOptions` configure `.tar`, `.tar.gz`, and `.tar.br` bundles |
| `Packata.Database` | `DubUrlConnectionService`, runtime configuration, connector availability/acquisition and connection sessions |
| `Packata.Provisioners` | `DubUrlProvisionerBuilder`, `DubUrlProvisioner`, schema plans and diagnostics, operation capabilities |

## Mapping and extension points

Call `ToCanonicalContract()` to map a native document into the shared model. The returned
`MappingResult<DataContract>` includes `Diagnostics`, `IsSuccessful` and `Value`. You can
also report diagnostics and require a value in one chain with `ReportDiagnostics(...).RequireValue()`.

Use `IDataContractMapper<T>` for lower-level adapters, dependency injection or third-party
formats. Namespaced `ExtensionMetadata` retains source-specific values, but you still need
to check which parts of the document the mapping supports.

Inference returns a `DataContractInferenceResult`. Inspect its `Value` and `Diagnostics`,
check `IsSuccessful` or `HasWarnings`, or use `RequireContract()` when a result is required.

Configure CSV sampling through `CsvDataContractInferenceOptions.InferenceOptions` and database
discovery through `DatabaseDataContractInferenceOptions.DiscoveryOptions`. Both allow metadata
overrides for generated assets and endpoints. The [data guide]({{ '/guides/data/' | relative_url }})
shows how inference fits into a workflow.

A canonical read request identifies the endpoint and can supply a schema and asset path.
To extend reading, provide a host stream resolver or register an `IDataEndpointReaderProvider`.
To customize parsing, use a delimited dialect or fixed-width layout resolver.
See the [reader and storage guide]({{ '/guides/readers-storage/' | relative_url }}) for configuration.

## Category label decoding

Configure `ResourceReaderFactory.Create(options => options.DecodeCategories = true)`
to register canonical `ValueDomain.Entries` labels as native PocketCsvReader
sequences for CSV/TSV/PSV. The default is false. Both ODCS enums and DPv2 inline
categories use this API. Local Category Tables recipe references also supply labels
when `DataEndpointReadRequest.Contract` supplies the containing contract. Lookups
are loaded once per reader opening with configurable row and byte limits; see
[category tables]({{ '/datapackage-categories/' | relative_url }}#category-tables-recipe).
Other engines
throw `CategoryDecodingNotSupportedException` before opening resources.

Columns with labels return `string`, including unmatched and unlabeled fallback
code text; columns without labels retain their original types. `GetFieldType`
and `GetDataTypeName` report this presentation type. `GetSchemaTable` remains
unimplemented by PocketCsvReader. Matching is exact and case-sensitive against
unquoted CSV text; quoted/escaped codes and alternate numeric spellings stay as
text. Null handling is unchanged. The source contract, logical types and allowed
codes are not modified. See the [two-format .NET examples]({{ '/quickstart/dotnet/' | relative_url }}#read-category-labels-from-either-standard).

**CSV cells to decode must be unquoted and unescaped.** PocketCsvReader skips
sequence replacement for fields it marks as quoted or escaped, regardless of
their parsed text. Codes requiring CSV quoting or escaping cannot be decoded
through this native feature. JSON/YAML descriptor quoting is unaffected.

## Comparison

Call `previous.Diff(candidate)` to obtain typed additions, removals and modifications,
including property type changes. Matching uses ordinal endpoint and asset IDs and field names.
A rename is therefore a removal plus an addition.

Named relationships match by name; unnamed relationships use composite identities.
Quality rules match by kind and expression. Missing or duplicate semantic identities fail.

Paths escape dots and backslashes. An added or removed item includes its complete subtree;
a modification preserves both previous and current values. Results are immutable snapshots,
ordered by ordinal path and then change kind.

Extension snapshots support canonical records, scalars, string-keyed dictionaries, sequences
and JSON. Unsupported custom mutable objects fail. The diff itself applies no compatibility
policy. See the [CLI change-kind table]({{ '/cli/' | relative_url }}#compare-two-contract-versions)
for the shared change vocabulary.

For a record-schema export, call `asset.ToJsonSchema()` and write it with
`schema.Serialize(stream)`. To export a selected CSV endpoint's dialect, use
`new CsvDialectExporter().Export(contract, endpointId)` and inspect its diagnostic result.
The [dialect export guide]({{ '/csv-dialect-export/' | relative_url }}) shows that workflow.

## Public source and runnable examples

- [Core contracts and mapping](https://github.com/Seddryck/Packata/tree/main/src/Packata.Core/Contracts)
- [Reader factory and resolvers](https://github.com/Seddryck/Packata/tree/main/src/Packata.ResourceReaders)
- [Storage builders](https://github.com/Seddryck/Packata/tree/main/src/Packata.Storages)
- [Database inference](https://github.com/Seddryck/Packata/tree/main/src/Packata.ResourceReaders.Database)
- [Database configuration/service](https://github.com/Seddryck/Packata/tree/main/src/Packata.Database)
- [Provisioning builder/plans](https://github.com/Seddryck/Packata/tree/main/src/Packata.Provisioners/Database)
- [DatabaseConnections example](https://github.com/Seddryck/Packata/tree/main/examples/DatabaseConnections): embedded database through both native standards.
- [ConnectorAcquisition example](https://github.com/Seddryck/Packata/tree/main/examples/ConnectorAcquisition): host-curated catalog/store, first-read acquisition and reuse; requires compatible catalog and runtime dependencies.

See [database integration]({{ '/database-integration/' | relative_url }}) for execution prerequisites and lifetime rules, [provisioning]({{ '/guides/provisioning/' | relative_url }}) for plan/deploy boundaries.

## Consumer compatibility and value domains

Use `ContractCompatibility.Compare(previous, current)` to assess candidate guarantees for
baseline consumers under `baseline-consumers-v1`. When working with native documents,
supply `CompatibilityInput` wrappers containing the matching canonical model, source standard,
mapping diagnostics and original descriptor tree.

Native comparisons support two ODCS inputs or two Data Package inputs; cross-standard analysis
fails. Results distinguish completed analysis, finding classifications and unknown coverage.
See the [policy and API details]({{ '/compatibility/' | relative_url }}) before using the result
as a compatibility gate.

Use `DataField.ValueDomain` to read typed scalar entries, optional labels and descriptions,
effective `AllowedValues`, ordering and references. For a category table,
`DataAsset.CategoryTable` identifies the logical value and label field paths.
ODCS Packata custom-property declarations map to these same types and use the
same resolver, without a dependency on Data Package models. Pass the containing
contract when reading referenced categories; see the
[ODCS declaration and binding example]({{ '/guides/data/' | relative_url }}#referenced-category-tables-in-odcs).
Its `LabelFields` collection declares alternate labels, while `DataField.Language`
identifies textual languages. Configure `ResourceReaderFactoryOptions.DecodeCategoriesLanguage`
(for example `"fr"`) or `DecodeCategoriesLabel` (for example `"short_label"`) to
select a table label. Either option enables decoding; they are mutually exclusive.
`CategoryDecodingDiagnostic` receives language-fallback warnings once per affected
table per opening. Supply the containing contract in `DataEndpointReadRequest.Contract`.
See the [selection and fallback rules]({{ '/datapackage-categories/' | relative_url }}#select-a-language-or-a-label-column).

Null membership means unknown membership, which differs from an explicit finite empty set.
Known allowed values supply the scalar enum constraint, while descriptive metadata remains
detached. See the [shared domain API]({{ '/value-domains/' | relative_url }}) for examples and limits.
