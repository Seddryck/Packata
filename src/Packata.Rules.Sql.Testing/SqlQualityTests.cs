using System.Data;
using Microsoft.Data.Sqlite;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.ServerTypes;
using CanonicalRule = Packata.Core.Contracts.DataQualityRule;

namespace Packata.Rules.Sql.Testing;

public class SqlQualityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private static QualityValue V(object? value) => QualityValue.From(value);
    private static CanonicalRule Rule(string query = "select 5", params QualityExpectation[] expectations)
        => new("sql") { SourceId = "quality", Target = new("asset", ["value"]), Definition = new SqlQuality(query), Expectations = expectations };
    private static DataContract Contract() => new(new("contract"), new(),
        [new("asset", "logical", "Order", null, AssetKind.Table,
            new([new("value", "integer", PhysicalName: "item value")]), [new("db")])],
        [new("db", null, EndpointKind.Database, null, new ConnectionLocation("sqlite", Namespace: "main"))], new());

    private sealed class Source(Func<DataEndpoint, CancellationToken, ValueTask<SqlRuleSession>> open) : ISqlRuleConnectionSource
    {
        public ValueTask<SqlRuleSession> OpenAsync(DataEndpoint endpoint, CancellationToken cancellationToken) => open(endpoint, cancellationToken);
    }
    private sealed class Fixture
    {
        public Mock<IDbConnection> Connection { get; } = new();
        public Mock<IDbCommand> Command { get; } = new();
        public List<IDataParameter> Parameters { get; } = [];
        public ISqlRuleConnectionSource Source { get; }
        public Fixture(IDataReader reader, bool owned = true, SqlReadOnlyCapability capability = SqlReadOnlyCapability.EnforcedByProvider)
        {
            Connection.Setup(c => c.CreateCommand()).Returns(Command.Object);
            Command.Setup(c => c.ExecuteReader()).Returns(reader);
            Command.Setup(c => c.CreateParameter()).Returns(() => new SqliteParameter());
            var parameters = new Mock<IDataParameterCollection>();
            parameters.Setup(p => p.Add(It.IsAny<object>())).Callback<object>(p => Parameters.Add((IDataParameter)p)).Returns(0);
            Command.SetupGet(c => c.Parameters).Returns(parameters.Object);
            Source = new Source((_, token) => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(new SqlRuleSession(Connection.Object, Quote, capability, owned)); });
        }
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static DataTable Table(params object?[] values)
    {
        var table = new DataTable(); table.Columns.Add("value", typeof(object));
        foreach (var value in values) table.Rows.Add(value ?? DBNull.Value);
        return table;
    }
    private static async Task<RuleResult> Execute(CanonicalRule rule, ISqlRuleConnectionSource source,
        SqlRuleOptions? options = null, RuleLimits? limits = null, DataContract? contract = null)
    {
        var compilation = new SqlQualityCompiler(source, options).Compile(rule, new(contract ?? Contract()));
        Assert.That(compilation.Diagnostics, Is.Empty);
        return (await RuleRunner.ExecuteAsync([compilation], new(Now, Limits: limits))).Results.Single();
    }

    [TestCase(ExpectationOperator.Equal, 5, null)]
    [TestCase(ExpectationOperator.NotEqual, 4, null)]
    [TestCase(ExpectationOperator.Greater, 4, null)]
    [TestCase(ExpectationOperator.GreaterOrEqual, 5, null)]
    [TestCase(ExpectationOperator.Less, 6, null)]
    [TestCase(ExpectationOperator.LessOrEqual, 5, null)]
    [TestCase(ExpectationOperator.Between, 4, 6)]
    [TestCase(ExpectationOperator.NotBetween, 1, 4)]
    public async Task ScalarMeasurementsUseEverySharedComparison(ExpectationOperator op, int operand, int? upper)
    {
        using var reader = Table(5m).CreateDataReader();
        var fixture = new Fixture(reader);
        var result = await Execute(Rule(expectations: [new(op, V(operand), upper is null ? null : V(upper))]), fixture.Source);
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Descriptor.Category, Is.EqualTo(RuleCategory.Quality));
        Assert.That(result.Descriptor.Backend, Is.EqualTo("sql"));
        Assert.That(result.Descriptor.EndpointId, Is.EqualTo("db"));
        Assert.That(result.ObservationTime, Is.EqualTo(Now));
        Assert.That(reader.IsClosed, Is.True);
        fixture.Command.Verify(c => c.Dispose(), Times.Once);
        fixture.Connection.Verify(c => c.Dispose(), Times.Once);
    }

    [Test]
    public async Task NumericPrecisionPercentUnitsAndMultipleExpectationsArePreserved()
    {
        const decimal value = 9007199254740993.000000000001m;
        using var reader = Table(value).CreateDataReader();
        var fixture = new Fixture(reader);
        var rule = Rule(expectations: [new(ExpectationOperator.Greater, V(9007199254740992L)), new(ExpectationOperator.Equal, V(value))])
            with { Definition = new SqlQuality("select measurement", "percent") };
        var result = await Execute(rule, fixture.Source);
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Value!.ToObject(), Is.EqualTo(value));
        Assert.That(result.Measurement.Unit, Is.EqualTo("percent"));
        Assert.That(result.Expectations, Has.Count.EqualTo(2));
    }

    [TestCase(true, RuleOutcome.Passed)]
    [TestCase(false, RuleOutcome.Failed)]
    public async Task BooleanWithoutExpectationUsesAnImplicitTrueAssertion(bool measured, RuleOutcome expected)
    {
        var fixture = new Fixture(Table(measured).CreateDataReader());
        Assert.That((await Execute(Rule(), fixture.Source)).Outcome, Is.EqualTo(expected));
        var explicitFalse = new Fixture(Table(false).CreateDataReader());
        Assert.That((await Execute(Rule(expectations: [new(ExpectationOperator.Equal, V(false))]), explicitFalse.Source)).Outcome, Is.EqualTo(RuleOutcome.Passed));
    }

    [Test]
    public async Task NumericMeasurementWithoutExpectationIsInconclusive()
    {
        var fixture = new Fixture(Table(1L).CreateDataReader());
        Assert.That((await Execute(Rule(), fixture.Source)).Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
    }

    [TestCase("empty", "SQL_EMPTY")]
    [TestCase("rows", "SQL_ROWS")]
    [TestCase("columns", "SQL_COLUMNS")]
    [TestCase("null", "SQL_NULL")]
    [TestCase("text", "SQL_TYPE")]
    [TestCase("nonfinite", "SQL_TYPE")]
    [TestCase("results", "SQL_RESULTS")]
    public async Task InvalidScalarShapesAreExplicitAndCannotPass(string shape, string code)
    {
        var table = shape switch { "empty" => Table(), "rows" => Table(1, 2), "null" => Table((object?)null),
            "text" => Table("1"), "nonfinite" => Table(double.NaN), _ => Table(1) };
        if (shape == "columns") table.Columns.Add("extra");
        using var reader = shape == "results" ? new DataTableReader([table, Table(2)]) : table.CreateDataReader();
        var fixture = new Fixture(reader);
        var result = await Execute(Rule(expectations: [new(ExpectationOperator.Equal, V(1))]), fixture.Source);
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That(result.Measurement.Diagnostics.Single().Code, Is.EqualTo(code));
        Assert.That(result.Measurement.Complete, Is.False);
        Assert.That(reader.IsClosed, Is.True);
        fixture.Connection.Verify(c => c.Dispose(), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CallerAndAdapterConnectionOwnershipIsRespectedOnErrors(bool owned)
    {
        var fixture = new Fixture(Table(1).CreateDataReader(), owned);
        fixture.Command.Setup(c => c.ExecuteReader()).Throws(new InvalidOperationException("sensitive provider detail"));
        var result = await Execute(Rule(), fixture.Source);
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Error));
        Assert.That(result.Measurement.Diagnostics.Single().Message, Does.Not.Contain("sensitive"));
        fixture.Command.Verify(c => c.Dispose(), Times.Once);
        fixture.Connection.Verify(c => c.Dispose(), owned ? Times.Once() : Times.Never());
    }

    [Test]
    public async Task ExplicitParametersTimeoutAndBorrowedTransactionAreApplied()
    {
        var fixture = new Fixture(Table(5).CreateDataReader(), owned: false);
        var transaction = new Mock<IDbTransaction>();
        var source = new Source((_, _) => ValueTask.FromResult(new SqlRuleSession(fixture.Connection.Object, Quote,
            SqlReadOnlyCapability.ReadOnlyCredentials, false, transaction.Object)));
        const string input = "quoted ' parameter ; remains data";
        var options = new SqlRuleOptions(Parameters: [new("@argument", V(input), DbType.String), new("@null", V(null))], CommandTimeoutSeconds: 30);
        await Execute(Rule("select @argument"), source, options, new(Timeout: TimeSpan.FromSeconds(3)));
        Assert.That(fixture.Parameters.Select(p => p.Value), Is.EqualTo(new object[] { input, DBNull.Value }));
        Assert.That(fixture.Parameters[0].DbType, Is.EqualTo(DbType.String));
        fixture.Command.VerifySet(c => c.CommandText = "select @argument", Times.Once);
        fixture.Command.VerifySet(c => c.CommandTimeout = 3, Times.Once);
        fixture.Command.VerifySet(c => c.Transaction = transaction.Object, Times.Once);
        fixture.Connection.Verify(c => c.Dispose(), Times.Never);
        transaction.Verify(t => t.Dispose(), Times.Never);
    }

    [Test]
    public async Task ReadOnlyCapabilityIsRequiredAndOwnedConnectionsAreReleased()
    {
        var fixture = new Fixture(Table(1).CreateDataReader(), capability: SqlReadOnlyCapability.Unknown);
        var result = await Execute(Rule(), fixture.Source);
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That(result.Measurement.Diagnostics.Single().Code, Is.EqualTo("SQL_READ_ONLY_REQUIRED"));
        fixture.Connection.Verify(c => c.CreateCommand(), Times.Never);
        fixture.Connection.Verify(c => c.Dispose(), Times.Once);
    }

    [Test]
    public async Task TimeoutsAndCallerCancellationAreDistinctAndNeverPass()
    {
        var source = new Source(async (_, token) => { await Task.Delay(1000, token); throw new InvalidOperationException(); });
        var result = await Execute(Rule(), source, limits: new(Timeout: TimeSpan.FromMilliseconds(10)));
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Inconclusive));
        Assert.That(result.Measurement.Diagnostics.Single().Code, Is.EqualTo("TIME_BUDGET"));
        var plan = new SqlQualityCompiler(source).Compile(Rule(), new(Contract())).Plans.Single();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.That(async () => await plan.ExecuteAsync(new(Now), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        var providerTimeout = new Source((_, _) => throw new TimeoutException("sensitive timeout details"));
        Assert.That((await Execute(Rule(), providerTimeout)).Outcome, Is.EqualTo(RuleOutcome.Error));
    }

    [Test]
    public void AmbiguousEndpointsMissingFieldsAndUnknownDialectsHaveCompilationFindings()
    {
        var source = new Fixture(Table(1).CreateDataReader()).Source;
        var contract = Contract();
        var ambiguous = contract with { Assets = [contract.Assets[0] with { EndpointBindings = [new("db"), new("other")] }],
            Endpoints = [.. contract.Endpoints, contract.Endpoints[0] with { Id = "other" }] };
        var compiler = new SqlQualityCompiler(source);
        Assert.That(compiler.Compile(Rule(), new(ambiguous)).Diagnostics.Any(d => d.Code == "SQL_ENDPOINT"), Is.True);
        Assert.That(compiler.Compile(Rule(), new(ambiguous, "other")).Plans, Has.Count.EqualTo(1));
        Assert.That(compiler.Compile(Rule(), new(contract, "absent")).Plans, Is.Empty);
        var duplicate = contract with { Assets = [contract.Assets[0] with { EndpointBindings = [new("db"), new("db")] }] };
        Assert.That(compiler.Compile(Rule(), new(duplicate, "db")).Diagnostics.Any(d => d.Code == "SQL_ENDPOINT_BINDING"), Is.True);
        Assert.That(compiler.Compile(Rule() with { Target = new("asset", ["missing"]) }, new(contract)).Diagnostics, Is.Not.Empty);
        var unknown = contract with { Endpoints = [contract.Endpoints[0] with { Location = new ConnectionLocation("unknown") }] };
        Assert.That(compiler.Compile(Rule(), new(unknown)).Diagnostics.Any(d => d.Code == "SQL_DIALECT"), Is.True);
        Assert.That(new SqlQualityCompiler(source, new(AuthoredDialect: SqlDialect.Ansi)).Compile(Rule(), new(unknown)).Plans, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NativeOdcsSqlRuleExecutesAgainstReadOnlySqliteWithPhysicalNamesAndParameters()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE \"Order\" (\"item value\" INTEGER); INSERT INTO \"Order\" VALUES (1), (2), (NULL); PRAGMA query_only=ON;";
            await setup.ExecuteNonQueryAsync();
        }
        var document = new OpenDataContract.DataContract { Id = "native", Servers = [new CustomServer { Server = "db", Type = "sqlite", Database = ":memory:", Schema = "main" }],
            Schema = [new() { Name = "asset", PhysicalName = "Order", Properties = [new() { Name = "value", PhysicalName = "item value", Quality =
                [new() { Id = "native-sql", Type = "sql", Query = "SELECT count(*) FROM {object} WHERE {property} > @minimum", MustBe = 1, Unit = "rows" }] }] }] };
        var canonical = new OpenDataContractMapper().Map(document).RequireValue();
        var rule = canonical.Assets[0].Schema!.Fields[0].QualityRules.Single();
        var source = new Source((_, _) => ValueTask.FromResult(new SqlRuleSession(connection, Quote,
            SqlReadOnlyCapability.EnforcedByProvider, ownsConnection: false)));
        var result = await Execute(rule, source, new(Parameters: [new("@minimum", V(1), DbType.Int64)]), contract: canonical);
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(result.Measurement.Value!.ToObject(), Is.EqualTo(1L));
        Assert.That(result.Measurement.Unit, Is.EqualTo("rows"));
        Assert.That(result.Descriptor.SourceId, Is.EqualTo("native-sql"));
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }
}
