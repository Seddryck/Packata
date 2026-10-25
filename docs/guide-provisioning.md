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

`PlanSchema` renders without deploying; `DeploySchema(plan)` executes that exact script without rendering it again. Use `DeploySchema(contract, options)` for direct schema deployment, `LoadDataAsync` for loading, or `ExecuteAsync` for deployment plus loading. Operation preflight checks known capabilities before DDL/source reads; it does not guarantee server permissions or transactional rollback.

Mapping includes physical table/column names and supported types, schema qualification, composite primary keys, foreign keys, range/length/pattern/enum constraints, and comments. Read diagnostics for unresolved identities, unsupported types/constraints and dialect limitations. Non-foreign-key relationships can be preserved without generating foreign-key DDL.

See [database integration]({{ '/database-integration/' | relative_url }}) for supported dialects, shared connections, credentials and bulk adapters, and [.NET reference]({{ '/reference/dotnet/' | relative_url }}) for entry points.

## CLI status

Command-line provisioning is separate [planned work](https://github.com/Seddryck/Packata/issues/476). No `packata provision` command is implemented. CLI schema inference and JSON Schema export do not deploy a database.
