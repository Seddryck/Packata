---
title: .NET library
parent: Reference
nav_order: 2
nav_exclude: false
permalink: /reference/dotnet/
---

Libraries target .NET 8, 9 and 10. Install only needed packages; standard-specific packages reference Core transitively. Start with the [runnable Quickstart]({{ '/quickstart/dotnet/' | relative_url }}). Public source links below provide API signatures; generated exhaustive API documentation is deferred.

| Package | Purpose / public entry points |
| --- | --- |
| `Packata.Core` | Canonical contracts, `DataContract`, mapping/inference results, diagnostics, typed differences, `IDataEndpointReaderFactory`, `IDataContractProvisioner` |
| `Packata.DataPackage` | Native v2 models, `DataPackageFactory`, JSON/YAML serializers and validators, `ToCanonicalContract`, `CsvDialectExporter` |
| `Packata.OpenDataContract` | Native ODCS models, `DataContractFactory`, YAML serializer/validation, `ToCanonicalContract` |
| `Packata.ResourceReaders` | `ResourceReaderFactory`, `CsvDataContractInference`, formats, custom providers and delimited dialect resolution |
| Reader extensions | [Format/package/registration table]({{ '/guides/readers-storage/' | relative_url }}); database extension also exposes `DatabaseDataContractInference` |
| `Packata.Storages` | `DataPackageLocatorBuilder`, `StorageProviderBuilder`, document containers, local/HTTP(S)/ZIP/cloud storage |
| `Packata.Database` | `DubUrlConnectionService`, runtime configuration, connector availability/acquisition and connection sessions |
| `Packata.Provisioners` | `DubUrlProvisionerBuilder`, `DubUrlProvisioner`, schema plans and diagnostics, operation capabilities |

## Mapping and extension points

`ToCanonicalContract()` returns a `MappingResult<DataContract>`; inspect `Diagnostics`, `IsSuccessful` and `Value`, or use `ReportDiagnostics(...).RequireValue()`. `IDataContractMapper<T>` is the lower-level adapter contract for dependency injection/third-party formats. Namespaced `ExtensionMetadata` preserves source-specific values; consumers must check mapping limitations.

`DataContractInferenceResult` exposes `Value`, `Diagnostics`, `IsSuccessful`, `HasWarnings` and `RequireContract()`. Configure CSV sampling via `CsvDataContractInferenceOptions.InferenceOptions`; database discovery via `DatabaseDataContractInferenceOptions.DiscoveryOptions`, with metadata overrides for generated assets/endpoints. See [data tasks]({{ '/guides/data/' | relative_url }}).

Canonical reader requests carry endpoint, optional schema and asset path. Host stream resolvers and registered `IDataEndpointReaderProvider` instances extend reading; delimited dialect and fixed-width layout resolvers extend parsing. See [readers/storage]({{ '/guides/readers-storage/' | relative_url }}).

## Comparison

`previous.Diff(candidate)` returns typed additions/removals/modifications, including property type changes. Matching uses ordinal endpoint/asset IDs and field names; renames become remove/add pairs. Named relationships use names, unnamed ones use composite identities; quality-rule identities use kind/expression. Missing/duplicate semantic identities fail.

Paths escape dots/backslashes. Added/removed subtrees are complete, modifications preserve previous/current values, and results are immutable snapshots sorted by ordinal path then kind. Supported extension snapshots include canonical records, scalars, string-keyed dictionaries, sequences and JSON; unsupported custom mutable objects fail. No compatibility policy is applied. See the [CLI payload-kind table]({{ '/cli/' | relative_url }}#compare-two-contract-versions) for the shared change vocabulary.

`asset.ToJsonSchema()` and `schema.Serialize(stream)` export one record schema. `new CsvDialectExporter().Export(contract, endpointId)` returns a diagnostic result for a selected CSV endpoint. See the [export guide]({{ '/csv-dialect-export/' | relative_url }}).

## Public source and runnable examples

- [Core contracts and mapping](https://github.com/Seddryck/Packata/tree/main/src/Packata.Core/Contracts)
- [Reader factory and resolvers](https://github.com/Seddryck/Packata/tree/main/src/Packata.ResourceReaders)
- [Storage builders](https://github.com/Seddryck/Packata/tree/main/src/Packata.Storages)
- [Database inference](https://github.com/Seddryck/Packata/tree/main/src/Packata.ResourceReaders.Database)
- [Database configuration/service](https://github.com/Seddryck/Packata/tree/main/src/Packata.Database)
- [Provisioning builder/plans](https://github.com/Seddryck/Packata/tree/main/src/Packata.Provisioners/Database)
- [DatabaseConnections example](https://github.com/Seddryck/Packata/tree/main/examples/DatabaseConnections): embedded database through both native standards.
- [ConnectorAcquisition example](https://github.com/Seddryck/Packata/tree/main/examples/ConnectorAcquisition): host-curated catalog/store, first-read acquisition and reuse; requires compatible catalog and runtime dependencies.

See [database integration]({{ '/database-integration/' | relative_url }}) for execution prerequisites and lifetime rules, [provisioning]({{ '/guides/provisioning/' | relative_url }}) for plan/deploy boundaries, and [migration]({{ '/migration/' | relative_url }}) for removed APIs.
