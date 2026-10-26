using System.Text;
using DubUrl;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Packata.Database;
using Packata.ResourceReaders;
using Packata.ResourceReaders.Database;
using Packata.Rules.Expressif;

namespace Packata.Rules.Sql.Testing;

public class QualityDifferentialTests
{
    private sealed class Streams(string text) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    }
    private sealed class DatabaseSource(string connectionString) : IDatabaseConnectionService
    {
        public ConnectionUrl Resolve(ConnectionLocation location, DatabaseRuntimeOptions? options = null) => throw new NotSupportedException();
        public async ValueTask<DatabaseConnectionSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return new(connection, name => "\"" + name.Replace("\"", "\"\"") + "\"");
        }
    }

    [Test]
    public async Task CatalogAndSqlMeasurementsAgreeAcrossCsvAndDatabaseReaders()
    {
        var connectionString = $"Data Source=quality-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        using (var setup = keeper.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE items (value TEXT); INSERT INTO items VALUES ('A'), ('A'), ('B'), ('N/A');";
            await setup.ExecuteNonQueryAsync();
        }
        var databases = new DatabaseSource(connectionString);
        var schema = new DataSchema([new("value", "string")]);
        var csv = new DataEndpoint("csv", null, EndpointKind.File, null, new PathLocation(["fixture.csv"]), new DataFormat("csv", Options: new Dictionary<string, object?> { ["lineTerminator"] = "\n" }));
        var database = new DataEndpoint("db", null, EndpointKind.Database, null, new ConnectionLocation("sqlite"), new DataFormat("database"));
        var sources = new IRuleDataSource[]
        {
            new ReaderRuleDataSource(new ResourceReaderFactory(new Streams("value\nA\nA\nB\nN/A\n")), "items", new(csv, schema), "csv-fixture"),
            new ReaderRuleDataSource(ResourceReaderFactory.Create(options => options.AddDatabase(databases)), "items", new(database, schema, "items"), "database-fixture")
        };
        var fixtures = new[]
        {
            ("rowCount", 4m, false, (Dictionary<string, QualityValue>?)null),
            ("nullValues", 0m, true, null),
            ("missingValues", 1m, true, new() { ["missingValues"] = QualityValue.From(new[] { "N/A" }) }),
            ("invalidValues", 1m, true, new() { ["validValues"] = QualityValue.From(new[] { "A", "B" }) }),
            ("duplicateValues", 1m, true, null)
        };
        var observedAt = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
        foreach (var (metric, expected, field, arguments) in fixtures)
        {
            var rule = new DataQualityRule("library") { SourceId = metric, Target = new("items", field ? ["value"] : []),
                Definition = new LibraryQuality("odcs", "3.2.0", metric, arguments),
                Expectations = [new(ExpectationOperator.Equal, QualityValue.From(expected))] };
            var compiled = new LibraryQualityCompiler().Compile(rule, new());
            Assert.That(compiled.Diagnostics, Is.Empty);
            foreach (var source in sources)
            {
                var report = await RuleRunner.ExecuteAsync([compiled], new(observedAt, source));
                Assert.That(report.Passed, Is.True, $"{metric}, {source.SnapshotId}: {string.Join("; ", report.Results.Select(r => $"{r.Outcome}: {r.Measurement.Value?.ToObject()}, {string.Join(",", r.Measurement.Diagnostics)}"))}");
                Assert.That(report.Results.Single().Measurement.Value!.ToObject(), Is.EqualTo(expected));
            }
        }
        var contract = new DataContract(new("fixture"), new(), [new("items", "items", "items", null, AssetKind.Table, schema, [new("db")])], [database], new());
        var sql = new DataQualityRule("sql") { SourceId = "rowCount", Target = new("items"), Definition = new SqlQuality("SELECT count(*) FROM {object}"),
            Expectations = [new(ExpectationOperator.Equal, QualityValue.From(4))] };
        var compilation = new SqlQualityCompiler(new DatabaseSqlRuleConnectionSource(databases, SqlReadOnlyCapability.ReadOnlyCredentials)).Compile(sql, new(contract));
        var sqlReport = await RuleRunner.ExecuteAsync([compilation], new(observedAt));
        Assert.That(sqlReport.Passed, Is.True);
        Assert.That(sqlReport.Results.Single().Measurement.Value!.ToObject(), Is.EqualTo(4L));
    }
}
