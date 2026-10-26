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

The diff example compares the sample with itself and returns `hasChanges: false`.
For two versions, supply the previous descriptor first and the candidate second. Finding
differences is a successful result and does not make the command fail.

The export example produces a JSON Schema for one record. It does not check every row in the
dataset. See the [CLI reference]({{ '/cli/' | relative_url }}) for the complete options and result formats.

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

Use `packata contract compare datapackage.json datapackage.json --output-format json --fail-on none`
to get a consumer-compatibility report without gating on its classifications. As with diff,
the baseline is the previous contract and the candidate is the current one.

In a .NET application, call `ContractCompatibility.Compare` with canonical models or native
`CompatibilityInput` wrappers. The analysis checks declared guarantees rather than actual
dataset contents. Turning off the gate does not make unknown coverage compatible.

Read the [compatibility policy]({{ '/compatibility/' | relative_url }}) for the direction and
assumptions, and the [compare reference]({{ '/cli/' | relative_url }}#assess-consumer-compatibility)
for every command option.