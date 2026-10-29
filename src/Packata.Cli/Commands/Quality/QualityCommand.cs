using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Packata.Database;
using Packata.Rules.Expressif;
using Packata.Rules.Sql;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class QualityCommands
{
    internal static void Configure(IConfigurator configuration) => configuration.AddBranch("quality", quality =>
    {
        quality.SetDescription("Execute ODCS quality rules and export OORS results.");
        quality.AddCommand<QualityCommand>("run").WithDescription("Run selected quality rules and produce an OORS v0.8.0 report.")
            .WithExample(["quality", "run", "contract.odcs.yaml", "--asset", "customers"]);
    });
}

internal sealed class QualityCommand : AsyncCommand<QualitySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, QualitySettings settings, CancellationToken cancellationToken)
    {
        var clock = TimeProvider.System;
        var start = clock.GetUtcNow();
        var timestamp = clock.GetTimestamp();
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken, retainArchive: true).ConfigureAwait(false);
            if (source.NativeDocument is not Packata.OpenDataContract.DataContract odcs)
                throw new ArgumentException("quality run requires an ODCS contract.");
            var validation = ContractValidation.Validate(source);
            foreach (var diagnostic in validation) CommandOutput.Diagnostic(diagnostic);
            if (validation.Any(d => d.Severity == MappingSeverity.Error)) return 2;
            var mapped = source.Map();
            if (!mapped.IsSuccessful) throw new InvalidOperationException("The contract cannot be processed.");
            var contract = mapped.RequireValue();
            // Validate selection before acquiring database configuration or data resources.
            IReadOnlyList<DataQualityRule> selected;
            try { selected = QualityRun.Select(contract, settings.Filters); }
            catch (ArgumentException) { throw new ContractOperationException("QUALITY002", "$", "No quality rules match the supplied filters."); }
            CliDatabase.ResolveOverrides(contract.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            using var database = selected.Any(r => r.Definition is not TextQuality) && contract.Endpoints.Any(e => e.Location is ConnectionLocation)
                ? new CliDatabase(new CliConfiguration(), source.BaseUri.IsFile ? source.BaseUri.LocalPath : "",
                    contract.Endpoints, settings.Connectors, settings.ConnectionEnvironment) : null;
            var streams = source.Archive is null ? null : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor!);
            var readers = PreviewService.CreateFactory(source.BaseUri, database?.RuntimeConnectionsService, streams);
            var engines = CreateEngines(database is null ? null : new SqlConnections(database, settings.SqlReadOnlyCredentials));
            var runner = new QualityRun(engines);
            RuleExecutionContext Context(DataQualityRule rule) => new(start,
                Data: new ContractDataSource(contract, readers, source.Source));
            var report = await runner.ExecuteAsync(contract, settings.Filters, Context, cancellationToken, clock, start, timestamp).ConfigureAwait(false);
            var document = OorsReport.Create(report, contract, odcs.ApiVersion, source, settings.Filters);
            OorsReport.Stamp(document, start, clock.GetUtcNow(), clock.GetElapsedTime(timestamp));
            var rendered = OorsReport.Serialize(document, settings.OutputFormat);
            cancellationToken.ThrowIfCancellationRequested();
            using var output = new OutputDestination(settings.Output);
            output.Writer.WriteLine(rendered);
            output.Commit();
            return report.ExitCode;
        }
        catch (Exception exception)
        {
            // Never echo arbitrary parser, runtime, SQL or provider messages.
            var message = exception is ContractOperationException operation ? operation.Diagnostic.Message
                : exception is OperationCanceledException ? "Quality run cancelled."
                : "Unable to complete the quality run or produce a valid OORS report. Check the contract, bindings and output destination.";
            CommandOutput.Diagnostic(new("QUALITY001", MappingSeverity.Error, "$", message));
            return 2;
        }
    }

    internal static QualityEngine[] CreateEngines(ISqlRuleConnectionSource? connections) =>
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
            var asset = ContractSelection.Asset(contract, target.AssetId);
            return readers.OpenAsync(PreviewService.Request(contract, asset), cancellationToken);
        }
    }
    private sealed class SqlConnections(CliDatabase database, bool readOnlyCredentials) : ISqlRuleConnectionSource
    {
        public async ValueTask<SqlRuleSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken)
        {
            var location = (ConnectionLocation)database.RuntimeEndpoint(endpoint).Location;
            var scheme = new Uri(DatabaseEndpointResolver.BuildConnectionUrl(location)).Scheme;
            var sqlite = scheme is "sqlite" or "sqlite3";
            if (!sqlite && !readOnlyCredentials) throw new InvalidOperationException("Read-only credentials are required for this SQL provider.");
            // Enforce the provider's read-only open mode; a rule can turn query_only off again.
            if (sqlite)
                location = location with { ConnectionUrl = DatabaseEndpointResolver.BuildConnectionUrl(location,
                    new(Options: new Dictionary<string, string> { ["Mode"] = "ReadOnly" })) };
            var session = await database.Connections.OpenAsync(endpoint with { Location = location }, cancellationToken).ConfigureAwait(false);
            try
            {
                if (sqlite)
                {
                    using var command = session.Connection.CreateCommand();
                    command.CommandText = "PRAGMA query_only = ON";
                    command.ExecuteNonQuery();
                }
                return new(session.Connection, session.RenderIdentifier, sqlite ? SqlReadOnlyCapability.EnforcedByProvider : SqlReadOnlyCapability.ReadOnlyCredentials);
            }
            catch { session.Connection.Dispose(); throw; }
        }
    }
}
