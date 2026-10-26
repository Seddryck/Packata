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
