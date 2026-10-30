using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Packata.Rules.Expressif;
using Packata.Rules.Sql;

namespace Packata.Application;

public sealed record QualityPreparation(DataContract? Contract, IReadOnlyList<MappingDiagnostic> Diagnostics)
{
    public bool IsSuccessful => Contract is not null && Diagnostics.All(value => value.Severity != MappingSeverity.Error);
}

public sealed class QualitySelectionException() : ArgumentException("No quality rules match the supplied filters.");

/// <summary>Coordinates validation, selection, engine composition and explicit rule execution.</summary>
public sealed class QualityExecutionService(IEnumerable<QualityEngine> engines, IDataEndpointReaderFactory readers, TimeProvider? clock = null)
{
    public static QualityPreparation Prepare(ContractDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document.NativeDocument is not Packata.OpenDataContract.DataContract)
            throw new ArgumentException("quality run requires an ODCS contract.");
        var validation = ContractDocument.Validate(document.NativeDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (validation.Any(value => value.Severity == MappingSeverity.Error)) return new(null, validation);
        var mapped = ContractInspector.Inspect(document, cancellationToken).Mapping;
        if (!mapped.IsSuccessful) throw new InvalidOperationException("The contract cannot be processed.");
        return new(mapped.RequireValue(), validation);
    }

    public static IReadOnlyList<DataQualityRule> SelectRules(DataContract contract, QualityFilters filters)
    {
        try { return QualityRun.Select(contract, filters); }
        catch (ArgumentException) { throw new QualitySelectionException(); }
    }

    public async Task<QualityRunReport> RunAsync(DataContract contract, QualityFilters filters, string snapshot,
        CancellationToken cancellationToken = default, DateTimeOffset? startedAt = null, long? startedTimestamp = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = clock ?? TimeProvider.System;
        var start = startedAt ?? timer.GetUtcNow();
        var timestamp = startedTimestamp ?? timer.GetTimestamp();
        SelectRules(contract, filters);
        var runner = new QualityRun(engines);
        RuleExecutionContext Context(DataQualityRule rule) => new(start, Data: new ContractDataSource(contract, readers, snapshot));
        return await runner.ExecuteAsync(contract, filters, Context, cancellationToken, timer, start, timestamp).ConfigureAwait(false);
    }
    public static QualityEngine[] CreateEngines(ISqlRuleConnectionSource? connections) =>
    [
        new(new("packata.rules.sql", Version(typeof(SqlQualityCompiler)), ["sql"]), new SqlQualityCompiler(connections ?? new UnavailableSql())),
        new(new("packata.rules.expressif", Version(typeof(LibraryQualityCompiler)), ["library", "custom"],
            new("expressif", typeof(global::Expressif.Expression).Assembly.GetName().Version?.ToString() ?? "unknown")), new ExpressifCompiler())
    ];
    private static string Version(Type type) => type.Assembly.GetName().Version?.ToString() ?? "unknown";
    private sealed class ExpressifCompiler : IRuleCompiler<DataQualityRule>
    {
        public CompilationResult Compile(DataQualityRule rule, RuleCompilationContext context)
        {
            if (rule.Definition is CustomQuality custom && string.Equals(custom.Engine, "expressif", StringComparison.OrdinalIgnoreCase))
                return new([], [new(rule.ExecutionId, CompilationFindingKind.Invalid, "EXPRESSIF_PAYLOAD", "The Expressif rule payload is invalid.")]);
            return rule.Definition is LibraryQuality ? new LibraryQualityCompiler().Compile(rule, context)
                : new ExpressifQualityCompiler().Compile(rule, context);
        }
    }
    private sealed class UnavailableSql : ISqlRuleConnectionSource
    {
        public ValueTask<SqlRuleSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No database connection is available.");
    }
    private sealed class ContractDataSource(DataContract contract, IDataEndpointReaderFactory readers, string snapshot) : IRuleDataSource
    {
        public string SnapshotId => snapshot;
        public bool IsReplayable => true;
        public bool IsComplete => true;
        public ValueTask<System.Data.IDataReader> OpenAsync(QualityTarget target, string? endpointId, CancellationToken cancellationToken)
        {
            var asset = ContractInferenceService.SelectAsset(contract, target.AssetId);
            return readers.OpenAsync(DataPreviewService.SelectRequest(contract, asset), cancellationToken);
        }
    }

}
