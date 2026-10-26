---
layout: default
title: Rule execution
parent: .NET reference
---

# Rule execution

Packata keeps three declaration categories separate: schema constraints describe
permitted data, quality rules describe measurements and expectations, and SLA
commitments describe operational promises. Lifecycle dates are not executable rules.
Declarations live in `Packata.Core.Contracts` and never implement an execution interface.

The execution pipeline is declaration → compiler → measurement → assertion → report.
Compilers preserve source identity, logical target, semantic category, backend, and
implementation version. Library is a versioned catalog, with Expressif as its backend;
SQL is a separate backend. Neither mapping nor executing a rule activates scheduling.

`Packata.Core` owns backend-neutral execution contracts, typed comparisons and results.
Adapters live outside Core and supply prepared measurement plans. Every run supplies
its observation time, selected endpoint, data access, bindings and resource limits.
Plans may be reused; readers, connections and mutable accumulators belong to each run.

An assertion violation is Failed. Execution failures are Error. Missing or incomplete
observations are Inconclusive. Unsupported declarations remain compilation findings;
skipped work is retained. Completion and violations are reported independently, and
partial work cannot produce an overall pass. Evidence is bounded and excludes raw
data and connection secrets by default.

This delivery covers canonical quality declarations and quality execution (#592–#596).
Constraint compilation into the existing dataset validator (#597) and explicit SLA
observation bindings (#598) remain subsequent work under #591. Producer compatibility
and consumer satisfaction comparisons remain separate operations.

## Canonical quality migration

The legacy `DataQualityRule(Kind, Description, Expression)` constructor remains source
compatible. New consumers should use `Definition`, `Target`, `Expectations`, `Metadata`,
`SourceId` and `SourcePath`. Native ODCS objects and serializers remain separate.
Missing source IDs stay null; execution uses the deterministic source collection path.
Explicit IDs preserve diff identity when a query, expectation or metadata changes;
`QualityRuleModified` exposes those three change dimensions.

Definitions include Library, SQL, text, custom and explicit Expressif programs.
ODCS shorthand metrics resolve to catalog `odcs`, version `3.2.0`. `metric` wins over
deprecated `rule`; a diagnostic records conflicts. The original native declaration
and declared operands are retained under the `odcs` extension namespace. Unsupported
engines and malformed programs remain custom declarations with diagnostics. Nested
properties and array items preserve separate logical path segments. DPv2 does not
acquire a quality extension and constraints never become quality declarations.

Operands and arguments use immutable `QualityValue` snapshots. Scalar membership
uses `ScalarValue` (#575), so integer, number, string and boolean remain distinct.
Explicit typed dates, offset timestamps, times and `ContractDuration` are retained;
calendar months are not converted to elapsed days. Date/time strings stay strings.
Multiple expectations are conjunctive. Between/not-between use exclusive endpoints
as specified by [ODCS 3.2](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/data-quality/).
Malformed ranges and conflicting comparisons are diagnosed rather than dropped.
Scheduling metadata is descriptive only.
