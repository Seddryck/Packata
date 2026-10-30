using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Packata.Database;
using Packata.Application;
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
            var contract = ValidateContract(source);
            if (contract is null) return 2;
            // Validate selection before acquiring database configuration or data resources.
            var selected = SelectRules(contract, settings.Filters);
            CliDatabase.ResolveOverrides(contract.Endpoints, settings.ConnectionEnvironment);
            CliDatabase.ParsePairs(settings.Connectors, "--connector");
            using var database = CreateDatabase(source, contract, selected, settings);
            var readers = CreateReaders(source, database);
            var engines = QualityExecutionService.CreateEngines(database is null ? null : new SqlConnections(database, settings.SqlReadOnlyCredentials));
            var service = new QualityExecutionService(engines, readers, clock);
            var report = await service.RunAsync(contract, settings.Filters, source.Source, cancellationToken, start, timestamp).ConfigureAwait(false);
            WriteReport(report, contract, source, settings, clock, timestamp, cancellationToken);
            return report.ExitCode;
        }
        catch (Exception exception)
        {
            // Never echo arbitrary parser, runtime, SQL or provider messages.
            CommandOutput.Diagnostic(new("QUALITY001", MappingSeverity.Error, "$", FailureMessage(exception)));
            return 2;
        }
    }

    private static DataContract? ValidateContract(ContractSource source)
    {
        var preparation = QualityExecutionService.Prepare(source.Document ?? throw new InvalidOperationException("The descriptor has not been parsed."));
        foreach (var diagnostic in preparation.Diagnostics) CommandOutput.Diagnostic(diagnostic);
        return preparation.Contract;
    }

    private static IReadOnlyList<DataQualityRule> SelectRules(DataContract contract, QualityFilters filters)
    {
        try { return QualityExecutionService.SelectRules(contract, filters); }
        catch (QualitySelectionException error) { throw new ContractOperationException("QUALITY002", "$", error.Message); }
    }
    private static CliDatabase? CreateDatabase(ContractSource source, DataContract contract,
        IReadOnlyList<DataQualityRule> selected, QualitySettings settings)
        => selected.Any(r => r.Definition is not TextQuality) && contract.Endpoints.Any(e => e.Location is ConnectionLocation)
            ? new CliDatabase(new CliConfiguration(), source.BaseUri.IsFile ? source.BaseUri.LocalPath : "",
                contract.Endpoints, settings.Connectors, settings.ConnectionEnvironment) : null;

    private static IDataEndpointReaderFactory CreateReaders(ContractSource source, CliDatabase? database)
    {
        var streams = source.Archive is null ? null : new PreviewService.ArchiveStreamResolver(source.Archive, source.ArchiveDescriptor!);
        return PreviewService.CreateFactory(source.BaseUri, database?.RuntimeConnectionsService, streams);
    }

    private static void WriteReport(QualityRunReport report, DataContract contract, ContractSource source,
        QualitySettings settings, TimeProvider clock, long timestamp, CancellationToken cancellationToken)
    {
        var odcs = (Packata.OpenDataContract.DataContract)source.NativeDocument;
        var document = OorsReport.Create(report, contract, odcs.ApiVersion, source, settings.Filters);
        OorsReport.Stamp(document, report.StartedAt, clock.GetUtcNow(), clock.GetElapsedTime(timestamp));
        var rendered = OorsReport.Serialize(document, settings.OutputFormat);
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new OutputDestination(settings.Output);
        output.Writer.WriteLine(rendered);
        output.Commit();
    }

    private static string FailureMessage(Exception exception) => exception switch
    {
        ContractOperationException operation => operation.Diagnostic.Message,
        OperationCanceledException => "Quality run cancelled.",
        _ => "Unable to complete the quality run or produce a valid OORS report. Check the contract, bindings and output destination."
    };

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
