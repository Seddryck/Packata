---
title: Inspect, validate, compare and export
parent: Guides
nav_order: 1
nav_exclude: false
permalink: /guides/contracts/
---

Load a contract, validate the descriptor, map to the canonical model, and select an asset or comparison pair. Metadata tasks do not open referenced data. Use the [Quickstart sample]({{ '/quickstart/cli/' | relative_url }}) below.

## CLI

```console
packata contract validate datapackage.json --output-format json
packata contract inspect datapackage.json --asset customers --output-format json
packata contract diff datapackage.json datapackage.json --output-format json
packata schema export datapackage.json -f json-schema --asset customers
```

Comparing the sample with itself using `contract diff` returns `hasChanges: false`. To compare versions, give the previous descriptor first and the candidate second. A diff with changes also exits successfully. JSON Schema export emits one record schema, not a dataset validation result. See [all CLI options and result contracts]({{ '/cli/' | relative_url }}).

## .NET

```csharp
using Packata.Core.Contracts;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Validation;

var native = new DataPackageFactory().LoadFromFile("datapackage.json");
var validation = DataPackageValidator.Validate(native);
var contract = native.ToCanonicalContract().RequireValue();
var asset = contract.Assets.Single(a => a.Id == "customers");
var diff = contract.Diff(contract); // previous.Diff(candidate)
var schema = asset.ToJsonSchema();
using var output = File.Create("customers.schema.json");
schema.Serialize(output);
```

Review validation issues and mapping diagnostics before continuing in production code. Source-standard validation and canonical comparison are distinct layers. ODCS uses its native serializer/validator and its mapping namespace. See [standards and limitations]({{ '/standards/' | relative_url }}) and [.NET API entry points]({{ '/reference/dotnet/' | relative_url }}).

## Assess consumer compatibility

Use `packata contract compare datapackage.json datapackage.json --output-format json --fail-on none` to request a directional report without gating on classifications. The baseline is previous and the candidate is current. In .NET, call `ContractCompatibility.Compare` with canonical models or native `CompatibilityInput` wrappers. This is implemented analysis of declared guarantees, not dataset validation. Unknown coverage remains unknown even when gating is disabled. See [compatibility policy]({{ '/compatibility/' | relative_url }}) and [all compare options]({{ '/cli/' | relative_url }}#assess-consumer-compatibility).
