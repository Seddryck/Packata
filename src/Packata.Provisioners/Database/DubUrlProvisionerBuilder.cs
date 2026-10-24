using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl;
using DubUrl.Mapping;
using Packata.Core.Provisioning;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.Database;

namespace Packata.Provisioners.Database;
public class DubUrlProvisionerBuilder : IDubUrlTypeStep, IDubUrlLocationStep, IDubUrlSettingsStep
{
    private string? DatabaseType { get; set; }
    private string? Server { get; set; } = string.Empty;
    private bool InMemory { get; set; } = false;
    private int? Port { get; set; } = null;
    private string? Database { get; set; } = null;
    private string? Username { get; set; } = null;
    private string? Password { get; set; } = null;
    private readonly IDatabaseConnectionService _connections;
    private readonly IDataEndpointReaderFactory? _readers;
    private readonly IDatabaseOperationCapabilities? _capabilities;

    public DubUrlProvisionerBuilder()
        : this(DubUrlConnectionService.Default) { }

    public DubUrlProvisionerBuilder(IDatabaseConnectionService connections,
        IDataEndpointReaderFactory? readerFactory = null, IDatabaseOperationCapabilities? capabilities = null)
        => (_connections, _readers, _capabilities) =
            (connections ?? throw new ArgumentNullException(nameof(connections)), readerFactory, capabilities);

    protected internal DubUrlProvisionerBuilder(SchemeRegistry schemeRegistry)
        : this(new DubUrlConnectionService(schemeRegistry)) { }

    public IDubUrlLocationStep Using(string databaseType)
    {
        DatabaseType = databaseType;
        return this;
    }

    IDubUrlSettingsStep IDubUrlLocationStep.OnServer(string serverName)
    {
        (Server, InMemory) = (serverName, false);
        return this;
    }

    IDubUrlSettingsStep IDubUrlLocationStep.InMemory()
    {
        (Server, InMemory) = (string.Empty, true);
        return this;
    }

    IDubUrlSettingsStep IDubUrlLocationStep.Local()
    {
        (Server, InMemory) = (".", false);
        return this;
    }

    IDubUrlSettingsStep IDubUrlSettingsStep.WithCredentials(string username, string password)
    {
        (Username, Password) = (username, password);
        return this;
    }

    IDubUrlSettingsStep IDubUrlSettingsStep.WithDatabase(string dbname)
    {
        (Database) = (dbname);
        return this;
    }

    IDubUrlSettingsStep IDubUrlSettingsStep.WithPort(int port)
    {
        (Port) = (port);
        return this;
    }

    protected IDataContractProvisioner Build()
    {
        if (DatabaseType is null)
            throw new InvalidOperationException("Database type must be specified.");
        if (string.IsNullOrEmpty(Server) && InMemory == false)
            throw new InvalidOperationException("Server must be specified or in-memry should be set to true.");

        var server = InMemory ? "memory" : Server;
        var url = _connections.Resolve(new ConnectionLocation(DatabaseType, server, Port, Database),
            new DatabaseRuntimeOptions(Username, Password));
        return new DubUrlProvisioner(url, readerFactory: _readers, capabilities: _capabilities);
    }

    IDataContractProvisioner IDataContractProvisionerBuilder.Build()
        => Build();
}
