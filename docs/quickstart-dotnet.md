---
title: .NET Library Quickstart
parent: Getting started
nav_order: 2
nav_exclude: false
permalink: /quickstart/dotnet/
---

Use a .NET 8, 9 or 10 SDK. This example loads and maps the same contract as the [CLI Quickstart]({{ '/quickstart/cli/' | relative_url }}), then reads its CSV through the shared reader.

```console
dotnet new console -n PackataQuickstart --framework net10.0
cd PackataQuickstart
dotnet add package Packata.DataPackage
dotnet add package Packata.ResourceReaders
```

Choose `net8.0` or `net9.0` instead if that is your installed SDK. `Packata.Core` comes transitively. Download [datapackage.json]({{ '/samples/quickstart/datapackage.json' | relative_url }}) and [customers.csv]({{ '/samples/quickstart/customers.csv' | relative_url }}) into this project directory.

Replace `Program.cs` with:

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

Run `dotnet run` from the project directory. Expected output:

```text
1: Alice
2: Bob
```

Dispose the reader when you finish, including when you stop early, because it owns the
streams it opens.

For ODCS, use its native loader and the `Packata.OpenDataContract.Mapping` adapter to reach
the same shared reader model. Continue with the [data guide]({{ '/guides/data/' | relative_url }})
or [.NET reference]({{ '/reference/dotnet/' | relative_url }}).