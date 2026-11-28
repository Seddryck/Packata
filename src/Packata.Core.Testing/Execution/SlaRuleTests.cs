using System.Runtime.CompilerServices;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Core.Testing.Execution;

public sealed class SlaRuleTests
{
    private static readonly DateTimeOffset Reference = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("Test UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");
    private static readonly ElementReference[] Orders = [new("orders")];
    private static readonly Sla Freshness = new(Freshness: [new(Hours(2), [new("orders", "loaded_at")])]);
    private static readonly SlaEvaluationBinding FreshnessBinding =
        Binding(SlaCommitmentKind.Freshness, SlaMeasurementPolicy.AgeOfLatestTimestamp) with
        {
            Target = new("orders", ["loaded_at"]),
            TimestampField = "loaded_at"
        };

    [TestCase(120, RuleOutcome.Passed)]
    [TestCase(121, RuleOutcome.Failed)]
    public async Task FreshnessMeasuresLatestBoundTimestampAtFixedReference(int ageMinutes, RuleOutcome expected)
    {
        var latest = Reference.AddMinutes(-ageMinutes).ToOffset(TimeSpan.FromHours(5.5));
        var result = await Run(Freshness, FreshnessBinding, Source(_ =>
            [Row(("loaded_at", latest)), Row(("loaded_at", Reference.AddHours(-3)), ("event_at", Reference))]));

        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Descriptor.SourceId, Is.EqualTo("orders-sla"));
        Assert.That(result.Descriptor.SourcePath, Is.EqualTo("/sla/freshness/0"));
        Assert.That(result.Descriptor.Category, Is.EqualTo(RuleCategory.Sla));
        Assert.That(result.Descriptor.SlaBinding, Is.EqualTo(FreshnessBinding));
        Assert.That(result.Descriptor.EndpointId, Is.EqualTo("production"));
        Assert.That(result.ObservationTime, Is.EqualTo(Reference));
        Assert.That(result.StartedAt, Is.EqualTo(Reference));
        Assert.That(result.Measurement.Population, Is.EqualTo(2));
        Assert.That(result.Measurement.Unit, Is.EqualTo("elapsed duration"));
        Assert.That(result.Measurement.SlaSnapshotId, Is.EqualTo("run-1"));
        Assert.That(result.Measurement.SlaWindow, Is.EqualTo(new SlaObservationWindow(Reference.AddHours(-24), Reference)));
        Assert.That(result.Measurement.Evidence.Single().Value.ToObject(), Is.EqualTo(latest));
        Assert.That(result.Measurement.Value?.ToObject(), Is.EqualTo(new ContractDuration(0, TimeSpan.FromMinutes(ageMinutes))));
    }

    [TestCase("null", "SLA_TIMESTAMP_MISSING_OR_INVALID")]
    [TestCase("missing", "SLA_TIMESTAMP_MISSING_OR_INVALID")]
    [TestCase("future", "SLA_OUT_OF_WINDOW_TIMESTAMP")]
    [TestCase("empty", "SLA_EMPTY_OBSERVATIONS")]
    [TestCase("unspecified", "SLA_UNSPECIFIED_TIMESTAMP_ZONE")]
    public async Task UnusableFreshnessObservationsAreInconclusive(string input, string diagnostic)
    {
        var rows = input switch
        {
            "empty" => [],
            "missing" => new[] { Row(("event_at", Reference)) },
            "future" => new[] { Row(("loaded_at", Reference)), Row(("loaded_at", Reference.AddTicks(1))) },
            "unspecified" => new[] { Row(("loaded_at", Reference.DateTime)) },
            _ => new[] { Row(("loaded_at", null)) }
        };
        CheckInconclusive(await Run(Freshness, FreshnessBinding, Source(_ => rows)), diagnostic);
    }

    [Test]
    public async Task DeclaredSourceZoneResolvesOffsetlessTimestamp()
    {
        var result = await Run(Freshness, FreshnessBinding with { SourceTimeZone = PlusTwo },
            Source(_ => [Row(("loaded_at", Reference.DateTime))]));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Value?.ToObject(), Is.EqualTo(Hours(2)));
    }

    [TestCase(2026, 3, 29)]
    [TestCase(2025, 10, 26)]
    public async Task DaylightSavingGapAndFoldDoNotGuessAnInstant(int year, int month, int day)
    {
        var wallTime = new DateTime(year, month, day, 2, 30, 0, DateTimeKind.Unspecified);
        var binding = FreshnessBinding with
        {
            SourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"),
            Lookback = Hours(24 * 200)
        };
        CheckInconclusive(await Run(Freshness, binding, Source(_ => [Row(("loaded_at", wallTime))])), "SLA_AMBIGUOUS_LOCAL_TIME");
    }

    [Test]
    public async Task CalendarThresholdUsesLocalMonthAndElapsedThresholdUsesTicks()
    {
        var instant = Reference.AddDays(-30);
        var source = Source(_ => [Row(("loaded_at", instant))]);
        var binding = FreshnessBinding with { Lookback = new(2, TimeSpan.Zero), CalendarTimeZone = PlusTwo };
        var calendar = Freshness with { Freshness = [Freshness.Freshness[0] with { Duration = new(1, TimeSpan.Zero) }] };
        var elapsed = Freshness with { Freshness = [Freshness.Freshness[0] with { Duration = Hours(30 * 24) }] };
        var calendarResult = await Run(calendar, binding, source);
        Assert.That(calendarResult.Outcome, Is.EqualTo(RuleOutcome.Passed)); // March 31 back to February 28 is 31 days.
        Assert.That(calendarResult.Expectations.Single().Operand.ToObject(), Is.EqualTo(new ContractDuration(1, TimeSpan.Zero)));
        var older = Source(_ => [Row(("loaded_at", instant.AddTicks(-1)))]);
        Assert.That((await Run(elapsed, binding, older)).Outcome, Is.EqualTo(RuleOutcome.Failed));
        Assert.That((await Run(calendar, binding, older)).Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(Compile(calendar, binding with { CalendarTimeZone = null }).Diagnostics.Single().Code, Is.EqualTo("MISSING_SLA_TIME_ZONE"));
    }

    [TestCase("stale", "SLA_SNAPSHOT_MISMATCH")]
    [TestCase("wrong id", "SLA_SNAPSHOT_MISMATCH")]
    [TestCase("partial coverage", "SLA_PARTIAL_WINDOW")]
    [TestCase("partial read", "SLA_PARTIAL_WINDOW")]
    [TestCase("missing", "SLA_OBSERVATIONS_MISSING")]
    public async Task FreshDataCannotPassAgainstAnUnprovenSnapshot(string scenario, string diagnostic)
    {
        var source = new ObservationSource(request =>
        {
            var snapshot = Snapshot(request, [Row(("loaded_at", Reference))]);
            return scenario switch
            {
                "stale" => snapshot with { AsOf = Reference.AddTicks(-1) },
                "wrong id" => snapshot with { SnapshotId = "different" },
                "partial coverage" => snapshot with { Coverage = request.Window with { Start = request.Window.Start.AddTicks(1) } },
                "partial read" => snapshot with { Complete = false },
                _ => null
            };
        });
        CheckInconclusive(await Run(Freshness, FreshnessBinding, source), diagnostic);
    }

    [Test]
    public async Task MidStreamTruncationAndRowBudgetDisposeTheEnumerator()
    {
        var disposed = 0;
        var source = new ObservationSource(request => Snapshot(request, []) with { Rows = PartialRows() });
        var partial = await Run(Freshness, FreshnessBinding, source);
        CheckInconclusive(partial, "PARTIAL_READ");
        var limited = await Run(Freshness, FreshnessBinding, source, new(MaxRows: 1));
        CheckInconclusive(limited, "ROW_BUDGET");
        Assert.That(disposed, Is.EqualTo(2));
        Assert.That(limited.Measurement.SlaWindow, Is.Not.Null);

        async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> PartialRows()
        {
            try
            {
                await Task.Yield();
                yield return Row(("loaded_at", Reference));
                yield return Row(("loaded_at", Reference));
                throw new IncompleteRuleInputException("PARTIAL_READ");
            }
            finally
            {
                disposed++;
            }
        }
    }

    [Test]
    public async Task CancellationReachesTheEnumeratorAndReleasesIt()
    {
        using var cancellation = new CancellationTokenSource();
        var disposed = false;
        var source = new ObservationSource(request => Snapshot(request, []) with { Rows = CancelDuringRead() });
        Assert.That(async () => await Run(Freshness, FreshnessBinding, source, token: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(disposed, Is.True);

        async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CancelDuringRead([EnumeratorCancellation] CancellationToken token = default)
        {
            try
            {
                yield return Row(("loaded_at", Reference));
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                disposed = true;
            }
        }
    }

    [Test]
    public async Task EvidenceLimitDoesNotRemoveWindowOrChangeTheOutcome()
    {
        var result = await Run(Freshness, FreshnessBinding, Source(_ => [Row(("loaded_at", Reference))]), new(MaxRows: 1, MaxEvidence: 0));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Evidence, Is.Empty);
        Assert.That(result.Measurement.SlaWindow, Is.Not.Null);
    }

    [Test]
    public async Task TimeBudgetMakesAnUnfinishedObservationInconclusive()
    {
        var result = await Run(Freshness, FreshnessBinding, new WaitingObservationSource(), new(Timeout: TimeSpan.FromMilliseconds(20)));
        CheckInconclusive(result, "TIME_BUDGET");
        Assert.That(result.Measurement.SlaWindow, Is.Not.Null);
    }

    [Test]
    public async Task CompilationRetainsMissingDuplicateUnsupportedAndUnmatchedBindings()
    {
        var sla = Freshness with
        {
            Availability = new(Windows: [new(new TimeOnly(9, 0), null, null, Orders)]),
            Latency = [new(Hours(1), Orders)],
            Retention = [new(Hours(2), Orders)]
        };
        var compilation = Compile(sla, FreshnessBinding, FreshnessBinding,
            Binding(SlaCommitmentKind.Latency, SlaMeasurementPolicy.AgeOfLatestTimestamp),
            FreshnessBinding with { Commitment = new(SlaCommitmentKind.Freshness, 9) });
        var report = await RuleRunner.ExecuteAsync([compilation], new(Reference));
        Assert.That(report.Results, Is.Empty);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.CompilationFindings.Select(finding => finding.Code), Is.EquivalentTo(new[]
        {
            "AMBIGUOUS_SLA_BINDING", "UNSUPPORTED_SLA_POLICY", "MISSING_SLA_BINDING", "UNSUPPORTED_SLA_WINDOW", "UNKNOWN_SLA_COMMITMENT"
        }));
        Assert.That(Compile(Freshness, FreshnessBinding with { Target = new("other") }).Diagnostics.Single().Code, Is.EqualTo("SLA_TARGET_MISMATCH"));
        Assert.That(Compile(Freshness, FreshnessBinding with { TimestampField = "" }).Diagnostics.Single().Kind, Is.EqualTo(CompilationFindingKind.MissingBinding));
    }

    [TestCase(60, RuleOutcome.Passed)]
    [TestCase(61, RuleOutcome.Failed)]
    [TestCase(-1, RuleOutcome.Inconclusive)]
    public async Task LatencyUsesRelatedEventsAndDeliveryWindow(int minutes, RuleOutcome expected)
    {
        var sla = new Sla(Latency: [new(Hours(1), Orders)]);
        var binding = Binding(SlaCommitmentKind.Latency, SlaMeasurementPolicy.MaximumPairedDeliveryLatency);
        var source = Source(request => [Interval(request.Window.Start.AddMinutes(-minutes), request.Window.Start)]);
        Assert.That((await Run(sla, binding, source)).Outcome, Is.EqualTo(expected));
        Assert.That(Compile(sla, binding with { EndTimestampField = null }).Diagnostics.Single().Kind, Is.EqualTo(CompilationFindingKind.MissingBinding));
    }

    [TestCase(0, RuleOutcome.Passed)]
    [TestCase(-1, RuleOutcome.Failed)]
    [TestCase(1, RuleOutcome.Inconclusive)]
    public async Task FrequencyRequiresAnAnchorAndIncludesTheTrailingGap(int anchorMinutes, RuleOutcome expected)
    {
        var sla = new Sla(Frequency: [new(Hours(12), Orders)]);
        var binding = Binding(SlaCommitmentKind.Frequency, SlaMeasurementPolicy.MaximumTimeSinceUpdate);
        var source = Source(request =>
        [
            Row(("start", request.Window.Start.AddMinutes(anchorMinutes))),
            Row(("start", request.Window.Start.AddHours(12)))
        ]);
        Assert.That((await Run(sla, binding, source)).Outcome, Is.EqualTo(expected));
        var earlyUpdate = Source(request => [Row(("start", request.Window.Start)), Row(("start", request.Window.Start.AddHours(11)))]);
        Assert.That((await Run(sla, binding, earlyUpdate)).Outcome, Is.EqualTo(RuleOutcome.Failed));
    }

    [TestCase(12, RuleOutcome.Passed)]
    [TestCase(11, RuleOutcome.Failed)]
    public async Task RetentionUsesContinuousCoverageEndingAtTheReference(int finalHours, RuleOutcome expected)
    {
        var sla = new Sla(Retention: [new(Hours(12), Orders)]);
        var binding = Binding(SlaCommitmentKind.Retention, SlaMeasurementPolicy.ContinuousRetainedCoverage);
        var source = Source(request =>
            [Interval(request.Window.Start, request.Window.Start.AddHours(1)), Interval(request.Window.End.AddHours(-finalHours), request.Window.End)]);
        Assert.That((await Run(sla, binding, source)).Outcome, Is.EqualTo(expected));
        CheckInconclusive(await Run(sla, binding with { Lookback = Hours(11) }, source), "SLA_RETENTION_WINDOW_TOO_SHORT");
    }

    [TestCase(0, RuleOutcome.Passed)]
    [TestCase(1, RuleOutcome.Failed)]
    public async Task AvailabilityWeighsMonitoringIntervalsAtTheTickBoundary(int unavailableExtraTicks, RuleOutcome expected)
    {
        var sla = new Sla(Availability: new(Target: [new(75m, Orders)]));
        var binding = Binding(SlaCommitmentKind.AvailabilityTarget, SlaMeasurementPolicy.TimeWeightedAvailability);
        var source = Source(request =>
        {
            var split = request.Window.Start.AddHours(18).AddTicks(-unavailableExtraTicks);
            return [Interval(request.Window.Start, split, true), Interval(split, request.Window.End, false)];
        });
        var result = await Run(sla, binding, source);
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Measurement.Unit, Is.EqualTo("percent"));
        if (unavailableExtraTicks == 0)
            Assert.That(result.Measurement.Value?.ToObject(), Is.EqualTo(75m));
        CheckInconclusive(await Run(sla, binding, Source(request => [Interval(request.Window.Start.AddTicks(1), request.Window.End, true)])),
            "SLA_MONITORING_GAP");
        CheckInconclusive(await Run(sla, binding, Source(request => [Interval(request.Window.Start, request.Window.End)])),
            "SLA_MISSING_STATUS");
    }

    [TestCase(true, RuleOutcome.Passed)]
    [TestCase(false, RuleOutcome.Failed)]
    public async Task DailyWindowSelectsMostRecentCompletedOvernightOccurrence(bool available, RuleOutcome expected)
    {
        var window = new AvailabilityWindow(new(23, 0), new(13, 0), null, Orders);
        var sla = new Sla(Availability: new(Windows: [window]));
        var binding = Binding(SlaCommitmentKind.AvailabilityWindow, SlaMeasurementPolicy.ContinuousAvailabilityInDailyWindow)
            with { Lookback = null };
        var source = Source(request => [Interval(request.Window.Start, request.Window.End, available)]);
        Assert.That(Compile(sla, binding).Diagnostics.Single().Code, Is.EqualTo("MISSING_SLA_TIME_ZONE"));
        var result = await Run(sla, binding with { CalendarTimeZone = PlusTwo }, source);
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Measurement.SlaWindow, Is.EqualTo(new SlaObservationWindow(
            new(2026, 3, 30, 21, 0, 0, TimeSpan.Zero), new(2026, 3, 31, 11, 0, 0, TimeSpan.Zero))));
        var fixedOffset = sla with { Availability = new(Windows: [window with { UtcOffset = TimeSpan.Zero }]) };
        var yesterday = await Run(fixedOffset, binding, source);
        Assert.That(yesterday.Measurement.SlaWindow?.End, Is.EqualTo(new DateTimeOffset(2026, 3, 30, 13, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public async Task DailyWindowUsesElapsedMonitoringTimeAcrossDaylightSaving()
    {
        var sla = new Sla(Availability: new(Windows: [new(new(1, 0), new(4, 0), null, Orders)]));
        var binding = Binding(SlaCommitmentKind.AvailabilityWindow, SlaMeasurementPolicy.ContinuousAvailabilityInDailyWindow) with
        {
            CalendarTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"),
            Lookback = null
        };
        var source = Source(request => [Interval(request.Window.Start, request.Window.End, true)]);
        var result = await Run(sla, binding, source, reference: Reference.AddDays(-2));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.SlaWindow, Is.EqualTo(new SlaObservationWindow(
            new(2026, 3, 29, 0, 0, 0, TimeSpan.Zero), new(2026, 3, 29, 2, 0, 0, TimeSpan.Zero))));
    }

    private static CompilationResult Compile(Sla sla, params SlaEvaluationBinding[] bindings)
        => new SlaRuleCompiler(new("orders-sla", bindings)).Compile(sla, new(EndpointId: "production"));

    private static async Task<RuleResult> Run(Sla sla, SlaEvaluationBinding binding, ISlaObservationSource source,
        RuleLimits? limits = null, CancellationToken token = default, DateTimeOffset? reference = null)
    {
        var compilation = Compile(sla, binding);
        Assert.That(compilation.Diagnostics, Is.Empty);
        var time = reference ?? Reference;
        var context = new RuleExecutionContext(time, Limits: limits, Clock: new FixedClock(time)) { SlaObservations = new("run-1", source) };
        return (await RuleRunner.ExecuteAsync([compilation], context, token)).Results.Single();
    }

    private static void CheckInconclusive(RuleResult result, string diagnostic)
    {
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That(result.Measurement.Complete, Is.False);
        Assert.That(result.Measurement.Diagnostics.Single().Code, Is.EqualTo(diagnostic));
    }

    private static SlaEvaluationBinding Binding(SlaCommitmentKind kind, SlaMeasurementPolicy policy)
        => new(new(kind, 0), new("orders"), "production", "monitor", policy, "start", Hours(24), "end", "up");

    private static ContractDuration Hours(int hours) => new(0, TimeSpan.FromHours(hours));

    private static IReadOnlyDictionary<string, object?> Row(params (string Name, object? Value)[] cells)
        => cells.ToDictionary(cell => cell.Name, cell => cell.Value);

    private static IReadOnlyDictionary<string, object?> Interval(DateTimeOffset start, DateTimeOffset end, bool? up = null)
        => Row(("start", start), ("end", end), ("up", up));

    private static ObservationSource Source(Func<SlaObservationRequest, IReadOnlyDictionary<string, object?>[]> read)
        => new(request => Snapshot(request, read(request)));

    private static SlaObservationSnapshot Snapshot(SlaObservationRequest request, IReadOnlyDictionary<string, object?>[] rows)
        => new(request.SnapshotId, request.ObservationTime, request.Window, true, ReadAsync(rows));

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ReadAsync(IReadOnlyDictionary<string, object?>[] rows)
    {
        await Task.Yield();
        foreach (var row in rows)
            yield return row;
    }

    private sealed class FixedClock(DateTimeOffset reference) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => reference;
    }

    private sealed class ObservationSource(Func<SlaObservationRequest, SlaObservationSnapshot?> read) : ISlaObservationSource
    {
        public ValueTask<SlaObservationSnapshot?> ObserveAsync(SlaObservationRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(read(request));
    }

    private sealed class WaitingObservationSource : ISlaObservationSource
    {
        public async ValueTask<SlaObservationSnapshot?> ObserveAsync(SlaObservationRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
