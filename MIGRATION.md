# Migration to the canonical Core model

This is a major-version breaking change. `Packata.Core` is no longer the Data Package v2 object model. It contains the canonical asset, schema, field, endpoint, reader, provisioner, diagnostic, and extension contracts shared by every supported document format.

## Package and namespace changes

| Before | Now |
|---|---|
| `Packata.Core.DataPackage` | `Packata.DataPackage.DataPackage` |
| `Packata.Core.Resource` | `Packata.DataPackage.Resource` |
| `Packata.Core.Schema` | `Packata.DataPackage.Schema` |
| `Packata.Core.Field` and derived fields | `Packata.DataPackage.Field` and derived fields |
| `Packata.Core.Constraint` and dialect types | `Packata.DataPackage.Constraint` and dialect types |
| `Packata.Core.Serialization.*` for Data Package | `Packata.DataPackage.Serialization.*` |
| `IDataPackageContainer` | `IDocumentContainer` |
| `IDataPackageLocator` / `DataPackageHandle` | `IDocumentLocator` / `DocumentHandle` |
| mutable `IResourceReaderFactory` configure/build API | `Packata.Core.Reading.IDataEndpointReaderFactory.OpenAsync` |
| `IPackageProvisioner` | `Packata.Core.Provisioning.IDataContractProvisioner` |

The superseded `Packata.DataContractSpecification` package has been removed. Use `Packata.OpenDataContract` for ODCS.

## Canonical flow

Both document formats are adapters into the same runtime model:

```text
Data Package JSON/YAML -> Packata.DataPackage.DataPackage -> DataPackageMapper --+
                                                                            |
ODCS YAML -------------> Packata.OpenDataContract.DataContract -> mapper ----+-> Packata.Core.Contracts.DataContract
                                                                                 -> canonical readers/provisioners
```

Data Package example:

```csharp
using Packata.DataPackage.Mapping;

var native = new Packata.DataPackage.DataPackageFactory().LoadFromStream(stream);
var canonical = native
    .ToCanonicalContract()
    .ReportDiagnostics(diagnostic => Console.WriteLine(diagnostic.Message))
    .RequireValue();
```

ODCS example:

```csharp
using Packata.OpenDataContract.Mapping;

var native = odcsSerializer.Deserialize(reader, container, storageProvider);
var canonical = native
    .ToCanonicalContract()
    .ReportDiagnostics(diagnostic => Console.WriteLine(diagnostic.Message))
    .RequireValue();
```

`ToCanonicalContract()` is the consumer-facing API. `IDataContractMapper<T>.Map(...)` remains available as the lower-level adapter contract for dependency injection and third-party formats. Source-only information remains available under the `datapackage` or `odcs` extension namespace.
