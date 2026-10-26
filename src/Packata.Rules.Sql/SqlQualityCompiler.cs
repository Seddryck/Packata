#nullable enable
using System.Data;
using System.Data.Common;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Sql;

public sealed class SqlQualityCompiler(ISqlRuleConnectionSource connections, SqlRuleOptions? options = null) : IRuleCompiler<DataQualityRule>
{
    private readonly SqlRuleOptions _options = (options ?? new()) with
    {
        ObjectPath = options?.ObjectPath is { } objects ? Array.AsReadOnly(objects.ToArray()) : null,
        PropertyPath = options?.PropertyPath is { } properties ? Array.AsReadOnly(properties.ToArray()) : null,
        Parameters = Array.AsReadOnly((options?.Parameters ?? []).ToArray())
    };

    public CompilationResult Compile(DataQualityRule declaration, RuleCompilationContext context)
    {
        var rule = declaration.Snapshot();
        if (rule.Definition is not SqlQuality sql) return CompilationResult.Unsupported(rule, "An SQL definition is required.");
        var findings = QualityCompilation.Validate(rule).ToList();
        ValidateOptions(sql, rule.ExecutionId, findings);
        try
        {
            var binding = SqlCompilationBinding.Resolve(rule, context, _options, findings);
            if (findings.Count > 0) return new([], findings.AsReadOnly());
            return CreatePlan(rule, sql, binding!);
        }
        catch (ArgumentException)
        {
            return new([], [new(rule.ExecutionId, CompilationFindingKind.Invalid, "SQL_IDENTIFIERS", "SQL placeholders or physical field paths could not be bound safely.")]);
        }
    }

    private void ValidateOptions(SqlQuality sql, string identity, List<CompilationFinding> findings)
    {
        void Invalid(string code, string message) => findings.Add(new(identity, CompilationFindingKind.Invalid, code, message));
        if (_options.CommandTimeoutSeconds <= 0) Invalid("SQL_TIMEOUT", "A positive command timeout is required.");
        if (string.IsNullOrWhiteSpace(sql.Query)) Invalid("SQL_QUERY", "A nonempty SQL query is required.");
        var parameters = _options.Parameters!;
        if (parameters.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Value.Kind != QualityValueKind.Scalar)
            || parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != parameters.Count)
            Invalid("SQL_PARAMETERS", "Explicit value parameters require unique provider parameter names and scalar values.");
    }

    private CompilationResult CreatePlan(DataQualityRule rule, SqlQuality sql, SqlCompilationBinding binding)
    {
        var objectPath = _options.ObjectPath ?? binding.ObjectPath();
        var propertyPath = _options.PropertyPath ?? binding.PhysicalFieldPath(rule.Target!.FieldPath);
        // Validate lexical structure and required placeholders before acquiring a connection.
        _ = SqlPlaceholderBinder.Bind(sql.Query, binding.Dialect, objectPath, propertyPath, identifier => identifier);
        var descriptor = QualityCompilation.Descriptor(rule, "sql", $"packata-sql/1;{binding.Dialect}", binding.Endpoint.Id);
        return new([new MeasuredExecutableRule(descriptor, new(QualityInputScope.Observation, RequiresCompleteInput: false),
            new SqlMeasurement(connections, binding.Endpoint, sql, binding.Dialect, objectPath, propertyPath, _options),
            new ExpectationAssertionPlan(rule.Expectations, implicitBoolean: true))], []);
    }

    private sealed class SqlMeasurement(ISqlRuleConnectionSource connections, DataEndpoint endpoint, SqlQuality sql,
        SqlDialect dialect, IReadOnlyList<string> objects, IReadOnlyList<string> properties, SqlRuleOptions options) : IMeasurementPlan
    {
        public async ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.Limits.MaxRows < 1) throw new IncompleteRuleInputException("ROW_BUDGET");
            using var session = await connections.OpenAsync(endpoint, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (options.RequireReadOnly && session.ReadOnlyCapability == SqlReadOnlyCapability.Unknown)
                throw new IncompleteRuleInputException("SQL_READ_ONLY_REQUIRED");
            using var command = session.Connection.CreateCommand();
            Configure(command, session.Transaction, context.Limits, session.QuoteIdentifier);
            using var cancellation = cancellationToken.Register(() => TryCancel(command));
            using var reader = command is DbCommand asynchronous
                ? await asynchronous.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false) : command.ExecuteReader();
            cancellationToken.ThrowIfCancellationRequested();
            return await ReadScalar(reader, cancellationToken).ConfigureAwait(false);
        }

        private void Configure(IDbCommand command, IDbTransaction? transaction, RuleLimits limits, Func<string, string> quoteIdentifier)
        {
            command.Transaction = transaction;
            command.CommandTimeout = Math.Min(options.CommandTimeoutSeconds, (int)Math.Min(int.MaxValue, Math.Ceiling(limits.EffectiveTimeout.TotalSeconds)));
            command.CommandText = SqlPlaceholderBinder.Bind(sql.Query, dialect, objects, properties, quoteIdentifier);
            foreach (var parameter in options.Parameters!)
            {
                var bound = command.CreateParameter();
                bound.ParameterName = parameter.Name;
                bound.Value = parameter.Value.ToObject() ?? DBNull.Value;
                if (parameter.Type is { } type) bound.DbType = type;
                command.Parameters.Add(bound);
            }
        }

        private static void TryCancel(IDbCommand command)
        {
            try { command.Cancel(); }
            catch (Exception) { /* Cancellation cannot expose provider errors. */ }
        }

        private async ValueTask<RuleMeasurement> ReadScalar(IDataReader reader, CancellationToken cancellationToken)
        {
            if (reader.FieldCount != 1) return Invalid("SQL_COLUMNS", "An SQL measurement requires exactly one column.");
            if (!await ReadAsync(reader, cancellationToken).ConfigureAwait(false)) return Invalid("SQL_EMPTY", "An SQL measurement requires exactly one row.");
            var raw = reader.GetValue(0);
            if (await ReadAsync(reader, cancellationToken).ConfigureAwait(false)) return Invalid("SQL_ROWS", "An SQL measurement returned more than one row.");
            if (await NextResultAsync(reader, cancellationToken).ConfigureAwait(false)) return Invalid("SQL_RESULTS", "An SQL measurement returned multiple result sets.");
            if (raw is null or DBNull) return Invalid("SQL_NULL", "An SQL measurement cannot be null.");
            if (!ScalarValue.TryCreate(raw, out var scalar) || scalar.Kind is not (ScalarKind.Integer or ScalarKind.Number or ScalarKind.Boolean))
                return Invalid("SQL_TYPE", "An SQL measurement must be a supported numeric or boolean scalar.");
            return new(QualityValue.From(raw), sql.Unit, Population: 1,
                Evidence: [new("scalarRows", QualityValue.From(1))]);
        }
        private static RuleMeasurement Invalid(string code, string message) => new(null, Complete: false, Diagnostics: [new(code, message)]);
        private static async ValueTask<bool> ReadAsync(IDataReader reader, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = reader is DbDataReader asynchronous ? await asynchronous.ReadAsync(token).ConfigureAwait(false) : reader.Read();
            token.ThrowIfCancellationRequested(); return result;
        }
        private static async ValueTask<bool> NextResultAsync(IDataReader reader, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = reader is DbDataReader asynchronous ? await asynchronous.NextResultAsync(token).ConfigureAwait(false) : reader.NextResult();
            token.ThrowIfCancellationRequested(); return result;
        }
    }
}
