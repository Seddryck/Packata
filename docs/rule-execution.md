---
layout: default
title: Rule execution
parent: Reference
permalink: /reference/dotnet/rule-execution/
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

## Shared execution API

`IRuleCompiler<TDeclaration>.Compile` returns plans and structured findings. Text
quality and unimplemented custom engines use `UnsupportedQualityCompiler`. Consumers
must pass both plans and findings to `RuleRunner.ExecuteAsync`; a compilation finding
makes the aggregate incomplete. Error, Skipped and Inconclusive are not violations and
cannot produce a pass. Caller cancellation propagates; elapsed time budget exhaustion
returns Inconclusive. Numeric measurements without expectations are Inconclusive.
Boolean convenience is opt-in on `ExpectationAssertionPlan`, used consistently by the
Expressif predicate and SQL adapters. Explicit expectations always take precedence.

`MeasuredExecutableRule` combines `IMeasurementPlan` and `IAssertionPlan` and preserves
Constraint/Quality/Sla categories independently of backend. For example, a measurement
implementation can return `new RuleMeasurement(QualityValue.From(5), "rows", 5)` and
compose with `new ExpectationAssertionPlan([new(ExpectationOperator.Greater,
QualityValue.From(0))])` without any database or expression dependency.

Every execution takes a fixed `ObservationTime`, typed bindings and `RuleLimits`.
Plans execute serially in ordinal identity order; duplicate IDs are invalid. Row and
state limits are enforced at reader boundaries by adapters. The timeout is cooperative:
synchronous calls cannot be forcibly interrupted, but results are checked before they
are accepted. Evidence is truncated to the requested bound; adapters only emit summary
counts. Unexpected exception messages are omitted from normal diagnostics.

`ReaderRuleDataSource` bridges the existing endpoint reader factory. It owns no live
reader; each plan opens and disposes its own reader. The caller selects the asset,
endpoint request and snapshot ID and guarantees replay observes that same snapshot.
One-shot sources are marked `IsReplayable = false`; the runner skips subsequent scans.
`IsComplete = false` prevents an aggregate pass. No full dataset buffering is implicit.
Plans are reusable across concurrent runs, but each run needs separate data sources,
connections and mutable backend contexts. User-supplied measurement/assertion plans
must honor this contract and sanitize any diagnostics they explicitly return.

Numeric ordering compares exact decimal representations with arbitrary precision,
including large integers and finite floating values. Numeric equality is mathematical;
value-domain membership keeps `ScalarValue`'s typed distinction. String equality is
ordinal, while ordering strings, booleans or null is invalid. Offset timestamps compare
instants. Calendar and elapsed durations with different components are not ordered
without a reference date. Between is exclusive; every declared expectation must hold.

## Expressif adapter

`Packata.Rules.Expressif` pins Expressif **2.93.1**. It uses the verified
`Expression.Create`, `Predication.Create` and `WithContext(EvaluationContext)` APIs.
Prepared programs are immutable; each run binds an independent runtime context.
Typed declaration arguments and run bindings never become expression text.
`@observationTime` is reserved and receives the run's fixed timestamp.

An ODCS custom rule uses `type: custom`, `engine: expressif`, and a JSON string in
`implementation`, for example:

```json
{"version":"1","kind":"predicate","scope":"value","program":"positive"}
```

`kind` is `expression` or `predicate`; `scope` is `value`, `row`, `dataset` or
`observation`. This payload is a Packata extension, not an ODCS program grammar.
The original payload remains available. Value targets require a logical field path.
Value/row programs apply expectations per item and reduce to an all-items verdict,
with violation and population counts. Empty item populations are Inconclusive.
Predicates default to expected true; explicit boolean equality expectations override
that convenience. Expressions require explicit expectations. Dataset programs receive
a single-use lazy row sequence and must consume it completely before they can pass.
They return one scalar measurement. Observation programs require explicit input.

The adapter permits a bounded set of arithmetic, text, null, Boolean and aggregation
operations listed in `PreparedQualityProgram`. IO, dynamic evaluation and arbitrary
host functions are unavailable. Unsupported operations become compilation findings.
Row records use logical names resolved from physical columns; nested record values
can be addressed by logical path segments. Array-item flattening is not implicit.
Reader budgets and cancellation are checked between rows; a synchronous Expressif
call cannot be interrupted midway. Scalar result/type errors remain execution errors.

### Package delivery decisions

| Dimension | Evidence and decision |
|---|---|
| Frameworks | Inherit net8.0, net9.0, net10.0 from repository properties; compile and test all three. |
| Deliverables | Portable managed adapter libraries; one NuGet identity per adapter with all three framework assemblies. |
| OS and RIDs | Reuse existing Windows library tests; no native adapter dependency or additional distribution RID. |
| Versions | Reuse the repository's single calculated release version and release-only publication boundary. |
| Prerequisites | Existing tracked icon/README; no additional generator or external tool. |
| Packaging | Include projects in library solution filter, compiled-output transfer, package list and artifact validator. |

## ODCS Library catalog

`LibraryQualityCompiler` resolves `IMetricCatalog` by exact catalog, version and name.
The shipped catalog is `odcs/3.2.0`, program version `packata-odcs/1`. A different
version is a different semantic contract, never a silent upgrade. Unknown versions,
metrics, arguments, scopes and units are compilation findings. Catalog definitions
carry argument schemas, result type, supported targets/units and Expressif projection,
selection, generic reduction and finalization programs. The compiler contains no
metric-name dispatch. An injected catalog may supply other definitions.

| Metric | Scope | Exact version 1 behavior |
|---|---|---|
| rowCount | Asset | Count every row, including rows with null fields. Rows only. Empty input is zero. |
| nullValues | Field | Count null/DBNull cells; empty strings and markers remain distinct. |
| missingValues | Field | Require `missingValues`, a typed scalar array; count exact typed membership. An empty array matches nothing. |
| invalidValues | Field | Require `validValues` and/or `pattern`; validity requires every supplied criterion. Pattern only matches strings; null is valid only with explicit membership and no pattern. |
| duplicateValues | Field or asset | Count excess occurrences after the first occurrence of each typed key. Asset scope requires non-empty `properties` containing logical top-level field names. Nulls participate in equality. |

All field metrics and composite duplicates support `rows` (default) and `percent`.
The denominator is every scanned row; percentage is `100 * numerator / denominator`.
Numerator/denominator are retained as bounded summary evidence. Zero-denominator
percentages are Inconclusive. Rows with missing physical fields cause Inconclusive;
source readers' explicit missing-value normalization precedes evaluation. Packata adds
no implicit equivalence between null, empty string, and a declared marker.

Membership and composite equality reuse `ScalarValue`, so integer 1, decimal 1 and
string "1" are different keys. Numeric expectation comparisons are mathematical.
The [ODCS metric reference](https://bitol-io.github.io/open-data-contract-standard/v3.2.0/data-quality/)
defines names, scopes and units; version 1 explicitly chooses excess duplicate counts,
null-key participation, and conjunctive validity where that reference is underspecified.

The pinned Expressif version lacks canonical typed-set membership and bounded regex
matching. The catalog adds reusable `TypedMembership` and `BoundedPattern` Expressif
predicates through a local factory/type mapper and composes them with
`PredicationBuilder`. Their arguments use typed context variables, never string
interpolation. Regex uses .NET culture-invariant semantics with a 100 ms match timeout.
There is no global registry mutation. Ordinary custom programs cannot invoke these
catalog-only predicates. Projection and percentage finalization use prepared Expressif
expressions; host machinery only scans, counts selections and groups keys.

Counting scans in constant space. Duplicate grouping and membership sets are bounded
by `MaxStateEntries`; row scans are bounded by `MaxRows`. Exhaustion is Inconclusive,
never an approximate pass. Readers are disposed on every exit. No scheduler, category
label decoding, implicit array flattening or SQL fallback is provided.

## SQL adapter

`Packata.Rules.Sql` compiles `SqlQuality` with `SqlQualityCompiler`. Supply a canonical
contract and select an endpoint when its asset has multiple bindings. Missing or
ambiguous targets, fields, endpoints and dialects are compilation findings. Recognized
endpoint schemes select SQLite, PostgreSQL, SQL Server, MySQL/MariaDB or DuckDB lexical
rules. Other schemes require an explicit `AuthoredDialect`. The adapter executes the
authored SQL directly: it performs no translation, transpilation, dialect conversion,
Expressif conversion or fallback to another database.

`{object}` and `{property}` bind only in SQL code, never inside literals, quoted
identifiers, line comments, block comments or PostgreSQL dollar-quoted strings.
Provider quoting applies to each physical identifier segment separately. Canonical
namespace/catalog and physical table/field names are resolved from the selected asset.
A canonical physical name is a single segment, including any literal dot; callers can
supply explicit `ObjectPath`/`PropertyPath` segment arrays for qualified identifiers.
MySQL lexical handling assumes its standard backslash escaping mode; callers must not
use this adapter for SQL modes with different string-escape rules. Vendor-specific
quoting beyond the documented forms needs an explicit adapter extension.

Only explicit `SqlValueParameter` bindings create provider parameters. Native arbitrary
quality arguments are never interpreted as SQL parameters. Values use the provider's
parameter mechanism, while identifier substitution uses its renderer. Connection
configuration and secrets stay in the host's connection service.

`ISqlRuleConnectionSource` supplies a per-run `SqlRuleSession`, optionally reusing
`DatabaseSqlRuleConnectionSource` and the existing `IDatabaseConnectionService`.
The session declares connection/transaction ownership. Commands and readers are always
disposed; borrowed connections and transactions remain open and caller-owned. Reused
plans require independent connections or caller-provided synchronization per run.
Command timeouts are bounded by the run time budget, cancellation is passed to async
provider operations and registered with `Cancel` for synchronous providers. Provider
support determines whether an in-progress call is interruptible.

Read-only capability is required by default, established through provider enforcement
or host-configured read-only credentials. A host may explicitly relax this requirement
for a trusted execution environment. SQL text inspection alone cannot establish that
a query is read-only. The adapter starts no transaction and provisions no constraints.

A measurement must return exactly one result set, one row and one column with a
supported finite numeric or boolean CLR value. Empty, multiple, null or incompatible
results are Inconclusive with a specific diagnostic; provider failures are Error.
Integers, decimals and finite floating types retain their supported precision.
Numeric measurements require expectations. Boolean measurements without expectations
implicitly assert true, consistent with Expressif predicates. Explicit expectations
always take precedence. Percent units are labels on an already authored measurement;
the adapter does not divide by a population again. Population is the scalar result-row
count, not an inferred underlying dataset size. Normal results contain bounded summary
evidence, never result-row data, SQL text, connection strings or provider messages.

The real-provider fixture uses Microsoft.Data.Sqlite 10.0.13 with provider-enforced
`PRAGMA query_only=ON`. The adapter itself depends on provider-neutral ADO.NET and the
existing Packata database service, not on SQLite. Other providers must provide the
authored dialect, identifier renderer and supported scalar CLR types; their live
integration suites have not been run for this delivery.

## Backend-independent executable example

This example requires only Packata.Core. The observation is supplied explicitly; no
scheduler or SLA monitor is implied.

```csharp
using Packata.Core.Contracts;
using Packata.Core.Execution;

var descriptor = new RuleDescriptor("count", null, "example/count", RuleCategory.Quality,
    new QualityTarget("orders"), "example", "1");
var rule = new MeasuredExecutableRule(descriptor,
    new InputRequirements(QualityInputScope.Observation), new SuppliedMeasurement(),
    new ExpectationAssertionPlan([new(ExpectationOperator.Greater, QualityValue.From(0))]));
var report = await RuleRunner.ExecuteAsync([new CompilationResult([rule], [])],
    new RuleExecutionContext(DateTimeOffset.Parse("2026-10-26T12:00:00Z"),
        Observation: QualityValue.From(5)));
// report.Passed is true. Omitting Observation produces Inconclusive.

sealed class SuppliedMeasurement : IMeasurementPlan
{
    public ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RuleMeasurement(context.Observation, "rows"));
    }
}
```
