using DubUrl;
using DubUrl.BulkCopy;
using DubUrl.Querying.Dialects;
using DubUrl.Schema;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Core.Reading;
using Packata.Database;

namespace Packata.Provisioners.Database;

public class DubUrlProvisioner : IDataContractProvisioner
{
    protected SchemaScriptRenderer ScriptRenderer { get; }
    protected SchemaScriptDeployer ScriptDeployer { get; }
    protected DbTypeMapper DbTypeMapper { get; }
    protected BulkCopyEngineFactory BulkCopyEngineFactory { get; }
    protected IDataEndpointReaderFactory? ReaderFactory { get; }
    protected IDatabaseOperationCapabilities Capabilities { get; }
    protected IDialect Dialect { get; }
    public ConnectionUrl ConnectionUrl { get; }

    protected internal DubUrlProvisioner(ConnectionUrl connectionUrl,
        SchemaScriptRenderer? scriptRenderer = null, SchemaScriptDeployer? deployer = null,
        DbTypeMapper? dbTypeMapper = null, BulkCopyEngineFactory? bulkCopyEngineFactory = null,
        IDataEndpointReaderFactory? readerFactory = null, IDatabaseOperationCapabilities? capabilities = null)
    {
        ConnectionUrl = connectionUrl;
        Dialect = GetDialect(connectionUrl);
        ScriptRenderer = scriptRenderer ?? new(Dialect, SchemaCreationOptions.None);
        ScriptDeployer = deployer ?? new();
        DbTypeMapper = dbTypeMapper ?? new();
        BulkCopyEngineFactory = bulkCopyEngineFactory ?? new();
        ReaderFactory = readerFactory;
        Capabilities = capabilities ?? new DubUrlOperationCapabilities();
    }

    public DubUrlProvisioner(ConnectionUrl connectionUrl)
        : this(connectionUrl, new(GetDialect(connectionUrl)), new(), new()) { }

    public DubUrlProvisioner(ConnectionUrl connectionUrl, IDataEndpointReaderFactory? readerFactory,
        IDatabaseOperationCapabilities? capabilities = null)
        : this(connectionUrl, null, null, null, null, readerFactory, capabilities) { }

    public DatabaseOperationSupport GetOperationSupport()
    {
        try { return Capabilities.GetSupport(ConnectionUrl); }
        catch (DatabaseConnectionException) { throw; }
        catch
        {
            throw new DatabaseConnectionException("database-preflight-failed",
                "Database operation support could not be resolved. Check the configured provider and its runtime prerequisites.");
        }
    }

    private void RequireOperation(DatabaseOperation operation)
    {
        if (!GetOperationSupport().Supports(operation))
            throw new DatabaseOperationNotSupportedException(operation, ConnectionUrl.Dialect.GetType().Name);
    }

    private static IDialect GetDialect(ConnectionUrl connectionUrl)
    {
        try { return connectionUrl.Dialect; }
        catch
        {
            var builder = new DialectRegistryBuilder(); builder.AddDialect<AnsiDialect>(["ansi"]);
            return builder.Build().Get<AnsiDialect>();
        }
    }

    public IReadOnlyList<ProvisioningDiagnostic> DeploySchema(DataContract contract,
        ContractProvisioningOptions? options = null)
    {
        var plan = PlanSchema(contract, options);
        DeploySchema(plan);
        return plan.Diagnostics;
    }

    public void DeploySchema(SchemaProvisioningPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ScriptDeployer.DeploySchema(ConnectionUrl, plan.Script);
    }

    public SchemaProvisioningPlan PlanSchema(DataContract contract, ContractProvisioningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        RequireOperation(DatabaseOperation.SchemaDeployment);
        return new RelationalSchemaPlanner(Dialect, ScriptRenderer, DbTypeMapper).PlanSchema(contract, options);
    }

    public async Task<IReadOnlyList<ProvisioningDiagnostic>> LoadDataAsync(DataContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();
        RequireOperation(DatabaseOperation.BulkLoading);
        var readerFactory = ReaderFactory ?? throw new InvalidOperationException(
            "A canonical endpoint reader factory is required to load data.");
        var diagnostics = new List<ProvisioningDiagnostic>();
        var resolver = new RelationalObjectNameResolver(Dialect, contract);
        using var bulkCopy = BulkCopyEngineFactory.Create(ConnectionUrl);
        foreach (var asset in contract.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = asset.EndpointBindings.FirstOrDefault();
            var endpoint = binding is null ? null : contract.Endpoints.FirstOrDefault(x => x.Id == binding.EndpointId);
            if (endpoint is null)
            {
                diagnostics.Add(new("PROV001", asset.Id,
                    "No resolvable endpoint binding is available; data loading was skipped."));
                continue;
            }
            var request = new DataEndpointReadRequest(endpoint, asset.Schema, binding!.AssetPath);
            using var reader = await readerFactory.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            var identity = resolver.Resolve(asset, diagnostics);
            bulkCopy.Write(Dialect.SchemaSqlRenderer.RenderObjectName(
                Dialect, identity.Catalog, identity.Database, identity.Schema, identity.Name), reader);
        }
        return diagnostics;
    }

    public async Task<IReadOnlyList<ProvisioningDiagnostic>> ExecuteAsync(DataContract contract,
        ContractProvisioningOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();
        RequireOperation(DatabaseOperation.BulkLoading);
        RequireOperation(DatabaseOperation.SchemaDeployment);
        if (ReaderFactory is null)
            throw new InvalidOperationException("A canonical endpoint reader factory is required to load data.");
        var diagnostics = new List<ProvisioningDiagnostic>(DeploySchema(contract, options));
        diagnostics.AddRange(await LoadDataAsync(contract, cancellationToken).ConfigureAwait(false));
        return diagnostics;
    }

}
