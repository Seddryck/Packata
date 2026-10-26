---
title: Plan and provision a database
parent: Guides
nav_order: 6
nav_exclude: false
permalink: /guides/provisioning/
---

You can use a data contract to generate database tables and constraints, review
the SQL, and then deploy it to a destination database. Packata provides these
operations through the .NET library. A
[CLI provisioning command is planned](https://github.com/Seddryck/Packata/issues/476);
`packata provision` is not implemented.

## Prepare the contract and destination

Load your descriptor and map it to Packata's shared contract model. The
[.NET Quickstart]({{ '/quickstart/dotnet/' | relative_url }}) shows how to load a
Data Package descriptor; the [.NET reference]({{ '/reference/dotnet/' | relative_url }})
also lists the ODCS entry points.

Configure a `DubUrlProvisioner` for the destination database. You will need a
connection service, a provider for that database, and any credentials required
by your application. Follow the
[database connection guide]({{ '/database-integration/' | relative_url }}) to set
these up. The examples below assume that `provisioner` is configured and
`contract` contains the mapped contract.

## Check which operations the provider supports

Before choosing an operation, inspect its capabilities:

```csharp
var support = provisioner.GetOperationSupport();
Console.WriteLine($"Schema deployment: {support.SchemaDeployment}");
Console.WriteLine($"Bulk loading: {support.BulkLoading}");
```

Schema deployment and bulk loading are separate capabilities. A provider may be
able to create tables without offering a bulk-loading adapter. Reading from a
database is another independent capability, so a working reader alone does not
establish deployment support.

The [preflight details]({{ '/database-integration/' | relative_url }}#provisioning-operation-preflight)
explain the supported dialects and how capabilities are determined.

## Generate and review the SQL

Create a plan before changing the destination:

```csharp
var plan = provisioner.PlanSchema(contract);
Console.WriteLine(plan.Script);
foreach (var diagnostic in plan.Diagnostics)
    Console.Error.WriteLine(diagnostic);
```

`PlanSchema` generates the SQL without deploying it. Read the script and diagnostics
and confirm that the destination is correct before proceeding.

The plan uses mapped physical table and column names, supported physical types,
and schema qualification. Depending on the dialect, it can include composite
primary keys, foreign keys, range, length, pattern and enum constraints, and comments.

Diagnostics identify unresolved names and unsupported types, constraints or dialect
features. A relationship in the contract produces foreign-key SQL only when it
represents a supported foreign key; other relationships remain in the model.

## Deploy the reviewed plan

When you are ready to apply the script, pass the plan you reviewed:

```csharp
provisioner.DeploySchema(plan);
```

Packata executes that plan's script without generating it again. If your application
does not need a separate review step, `DeploySchema(contract, options)` generates
and deploys directly.

Capability checks can reject an unsupported operation before SQL runs. They do
not establish that the database account has permission, that deployment will
succeed, or that a failure can be rolled back transactionally.

## Load data after deployment

Use `LoadDataAsync` to load data into the destination, or `ExecuteAsync` to combine
schema deployment and loading. The combined operation checks deployment support,
bulk-loading support and reader configuration before running SQL or reading source
data. Configure the source readers as described in the
[reader guide]({{ '/guides/readers-storage/' | relative_url }}).

For a complete runnable example, see `examples/DatabaseConnections` in the repository.
It creates a DuckDB table and reads it through both ODCS and Data Package contracts.
The [database guide]({{ '/database-integration/' | relative_url }}#examples-and-validation-scope)
provides the run command and explains connector setup and resource ownership.

CLI schema inference discovers metadata from an existing database, and JSON Schema
export describes a record. Neither command creates database tables.
