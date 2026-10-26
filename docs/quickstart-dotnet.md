---
title: .NET Library Quickstart
parent: Getting started
nav_order: 2
nav_exclude: false
permalink: /quickstart/dotnet/
---

Build a small C# application that loads a customer dataset's contract and prints
its records. It uses the same files as the
[CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}), but calls Packata's APIs
directly. You can follow this walkthrough without installing the CLI.

You need a .NET 8, 9 or 10 SDK and a terminal. The commands below use .NET 10;
choose `net8.0` or `net9.0` instead of `net10.0` if you are using one of those SDKs.

## Create the application

```console
dotnet new console -n PackataQuickstart --framework net10.0
cd PackataQuickstart
dotnet add package Packata.DataPackage
dotnet add package Packata.ResourceReaders
```

The first package loads Data Package contracts and maps them to Packata's shared
model. The second reads the data those contracts describe. `Packata.Core` is included
through the package dependencies.

## Download the sample

Save [datapackage.json]({{ '/samples/quickstart/datapackage.json' | relative_url }})
and [customers.csv]({{ '/samples/quickstart/customers.csv' | relative_url }}) in the
new project directory, beside `Program.cs`.

The contract describes an asset named `customers` with integer `id` and string
`name` fields. Its endpoint points to the CSV, whose two records describe Alice
and Bob. An asset is the dataset; the endpoint tells the reader where to find it.

## Load the contract and read its records

Replace `Program.cs` with the following code:

```csharp
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.DataPackage;
using Packata.DataPackage.Mapping;
using Packata.ResourceReaders;

var path = Path.GetFullPath("datapackage.json");
var contract = new DataPackageFactory().LoadFromFile(path)
    .ToCanonicalContract()
    .ReportDiagnostics(d => Console.Error.WriteLine($"{d.Severity}: {d.Message}"))
    .RequireValue();
var asset = contract.Assets.Single(a => a.Id == "customers");
var binding = asset.EndpointBindings.Single();
var endpoint = contract.Endpoints.Single(e => e.Id == binding.EndpointId);
// Resolve canonical relative paths against the descriptor directory explicitly.
var location = (PathLocation)endpoint.Location;
endpoint = endpoint with
{
    Location = new PathLocation(location.Paths
        .Select(p => Path.GetFullPath(p, Path.GetDirectoryName(path)!)).ToArray())
};
var readers = new ResourceReaderFactory();
using var reader = await readers.OpenAsync(
    new DataEndpointReadRequest(endpoint, asset.Schema, binding.AssetPath));
while (reader.Read())
    Console.WriteLine($"{reader.GetValue(0)}: {reader.GetValue(1)}");
```

The loader reads the descriptor, then `ToCanonicalContract()` converts it to the shared model.
Mapping diagnostics are printed to stderr so unsupported or incomplete metadata
is visible. `RequireValue()` stops the example if mapping did not produce a usable
contract.

The code selects the `customers` asset and its single binding, then finds the
corresponding endpoint. It resolves the CSV path against the descriptor's
directory, so the file location does not depend on where the application was
launched.

Finally, the reader factory opens that endpoint and returns an `IDataReader`. The
loop reads each record and prints its two field values. The `using` declaration
disposes the reader and its streams when the scope ends, including if reading
fails or stops early.

## Run and check the output

From the project directory, run:

```console
dotnet run
```

Expected output:

```text
1: Alice
2: Bob
```

If a sample file cannot be found, check that both downloads are beside
`Program.cs` and that the CSV filename matches the path in the contract.
The example reads the files without changing them.

## Continue with your application

For an ODCS descriptor, use its native loader and the
`Packata.OpenDataContract.Mapping` adapter to reach the same shared model. The
[.NET reference]({{ '/reference/dotnet/' | relative_url }}) lists those entry points.

Use the [data guide]({{ '/guides/data/' | relative_url }}) to infer a schema from
records, or the [reader guide]({{ '/guides/readers-storage/' | relative_url }})
to add formats beyond the built-in delimited and inline-data readers.
