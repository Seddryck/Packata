---
title: Plan and provision a database
parent: Guides
nav_order: 6
nav_exclude: false
permalink: /guides/provisioning/
---

Map a contract, choose a destination, inspect the schema plan and its diagnostics, then deploy the reviewed script. Schema deployment and bulk loading have independent capabilities.

## .NET

Given a configured `DubUrlProvisioner` and mapped `contract`:

```csharp
var support = provisioner.GetOperationSupport();
var plan = provisioner.PlanSchema(contract);
Console.WriteLine(plan.Script);
foreach (var diagnostic in plan.Diagnostics)
    Console.Error.WriteLine(diagnostic);
// After reviewing the destination, script and diagnostics:
provisioner.DeploySchema(plan);
```

Start with `PlanSchema` when you want to review the generated script before changing a database.
It renders the plan without deploying it. After review, `DeploySchema(plan)` executes that exact
script without rendering it again.

You can also deploy directly with `DeploySchema(contract, options)`, load data with
`LoadDataAsync`, or combine deployment and loading with `ExecuteAsync`. The operation preflight
checks known capabilities before running DDL or reading source data. It does not guarantee
server permissions, a successful deployment or transactional rollback.

The provisioner uses mapped physical table and column names, supported physical types and
schema qualification. It can generate composite primary keys, foreign keys, supported range,
length, pattern and enum constraints, and comments.

Review the diagnostics for names that could not be resolved and for unsupported types,
constraints or dialect features. A relationship that is not a foreign key can remain in the
model without producing foreign-key DDL.

See [database integration]({{ '/database-integration/' | relative_url }}) for supported dialects, shared connections, credentials and bulk adapters, and [.NET reference]({{ '/reference/dotnet/' | relative_url }}) for entry points.

## CLI status

Command-line provisioning is separate [planned work](https://github.com/Seddryck/Packata/issues/476). No `packata provision` command is implemented. CLI schema inference and JSON Schema export do not deploy a database.
