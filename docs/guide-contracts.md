---
title: Validate, inspect, diff and export contracts
parent: Guides
nav_order: 1
nav_exclude: false
permalink: /guides/contracts/
---

Before using a data contract, you may want to check that it is valid, explore its
contents, review changes to it, or export a schema for another tool. Each task has
its own command.

The examples use [datapackage.json]({{ '/samples/quickstart/datapackage.json' | relative_url }})
from the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}). Save it in your
working directory. It describes a `customers` dataset with two fields: an integer
`id` and a string `name`.

These commands read the contract itself. They do not open `customers.csv` or check
its rows. To read the data, use the [data guide]({{ '/guides/data/' | relative_url }}).

## Validate a contract

Use `contract validate` to check whether a descriptor follows its source standard.
For example, run it after editing a contract to catch invalid field types or
missing required metadata before sharing the file.

```console
packata contract validate datapackage.json
```

For this sample, Packata prints `Contract is valid.` and exits with code `0`.
If the document is invalid, it reports validation errors and exits with code `1`.
This checks the descriptor, so a valid result does not tell you whether the CSV
exists or its values match the schema.

Add `--output-format json` when you need a machine-readable validation report.
See the [validation reference]({{ '/cli/' | relative_url }}#validate-a-contract)
for supported sources, options and exit codes.

For ODCS, this command enforces selected descriptor checks. See the
[ODCS validation matrix]({{ '/standards/odcs-validation/' | relative_url }}) for
the exact JSON Schema, semantic and runtime coverage, version differences and
examples that demonstrate the limits of a successful result.

## Inspect the contract's contents

Use `contract inspect` to see how Packata understands the descriptor. This is useful
when you need to find an asset's identifier or check its fields, constraints and
connection details before choosing another command.

```console
packata contract inspect datapackage.json --asset customers
```

The output shows the `customers` asset, its `id` and `name` fields, and the
`customers-endpoint` endpoint. `--asset customers` selects that dataset; omit the
option to inspect all assets in the contract.

Add `--output-format json` to use the result in another program. Inspection presents
Packata's shared contract model, which gives ODCS and Data Package descriptors a
common structure. See the [inspection reference]({{ '/cli/' | relative_url }}#inspect-the-canonical-contract)
for all options and the JSON response format.

## Find changes with a diff

Use `contract diff` when reviewing an edited contract. It lists what was added,
removed or modified between two versions.

To try it, copy `datapackage.json` to `candidate.json`. In the copy, remove the
`name` field so that the `fields` array contains only:

```json
"fields": [
  { "name": "id", "type": "integer" }
]
```

Leave the rest of the descriptor unchanged, then run:

```console
packata contract diff datapackage.json candidate.json
```

Pass the old version first and the proposed version second. The report identifies
the removal of `name` from `customers`. Your original file still contains both fields.

For a JSON report, run:

```console
packata contract diff datapackage.json candidate.json --output-format json
```

The result has `hasChanges: true` and a `PropertyRemoved` entry in `changes`.
The command exits with code `0` because it finished generating the report. That
exit code does **not** mean the two contracts are identical or that the change is
safe. A script that needs to detect changes should read `hasChanges`.

Diff describes changes without judging their effect on consumers. For all options
and change types, see the [diff reference]({{ '/cli/' | relative_url }}#compare-two-contract-versions).

## Export a record schema

Use `schema export` when another tool needs a JSON Schema for records in one
dataset. Select the asset and save the generated schema to a new file:

```console
packata schema export datapackage.json --asset customers --output-format json-schema --output customers.schema.json
```

The generated JSON Schema Draft 2020-12 describes a record with an integer `id`
and a string `name`. Packata reads the original `datapackage.json`, so the field
removed from `candidate.json` is still present in this export.

Omit `--output customers.schema.json` to print the schema in the terminal. When
writing to a file, choose a path that does not already exist: Packata protects
existing files from being overwritten.

Export creates the schema; it does not run a validator against your data. A record
schema also cannot express every contract rule, such as uniqueness across all rows.
See the [schema export reference]({{ '/cli/' | relative_url }}#export-a-dataset-schema)
for supported types, constraints and options.

## Check whether a change could break consumers

After reviewing a diff, use the separate `contract compare` command to assess
whether existing consumers can safely use data described by the proposed contract.
Try it with the same two files:

```console
packata contract compare datapackage.json candidate.json
```

Removing `name` is classified as breaking: a consumer built against the original
contract may still expect that field. With the default settings, the command exits
with code `4`, allowing a CI job to stop the proposed change.

To get the report without stopping a job on its findings, add `--fail-on none`.
The removal is still reported as breaking, but a completed analysis exits with
code `0`. Errors such as an unreadable file still produce a nonzero exit code.

Compatibility analysis checks the guarantees declared in the contracts; it does
not test applications or read dataset rows. Read the
[compatibility policy]({{ '/compatibility/' | relative_url }}) for its assumptions
and limits, and the [compare reference]({{ '/cli/' | relative_url }}#assess-consumer-compatibility)
for all options and exit codes.

## Use these operations from .NET

In an application, load and validate the native descriptor before mapping it to
Packata's shared model. This example loads both versions and calculates their diff:

```csharp
using Packata.Core.Contracts;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Validation;

var native = new DataPackageFactory().LoadFromFile("datapackage.json");
var validation = DataPackageValidator.Validate(native);
// Review validation issues before using the contract.
var previous = native.ToCanonicalContract()
    .ReportDiagnostics(d => Console.Error.WriteLine($"{d.Severity}: {d.Message}"))
    .RequireValue();
var edited = new DataPackageFactory().LoadFromFile("candidate.json");
var candidateValidation = DataPackageValidator.Validate(edited);
// Review candidateValidation issues as well.
var candidate = edited.ToCanonicalContract()
    .ReportDiagnostics(d => Console.Error.WriteLine($"{d.Severity}: {d.Message}"))
    .RequireValue();

var asset = previous.Assets.Single(a => a.Id == "customers");
var diff = previous.Diff(candidate);
```

To export the original asset's record schema, add:

```csharp
var schema = asset.ToJsonSchema();
using var output = File.Create("customers.schema.json");
schema.Serialize(output);
```

`previous.Diff(candidate)` lists the changes. For consumer compatibility analysis,
use `ContractCompatibility.Compare` instead; the
[compatibility API guide]({{ '/compatibility/' | relative_url }}#reusable-api)
explains how to preserve source-standard information for that analysis.

ODCS has its own loader, validator and mapping adapter. See the
[.NET reference]({{ '/reference/dotnet/' | relative_url }}) for those entry points
and [standards and limitations]({{ '/standards/' | relative_url }}) for mapping coverage.
