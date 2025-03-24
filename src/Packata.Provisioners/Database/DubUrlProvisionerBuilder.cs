using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl;
using DubUrl.Mapping;
using Packata.Core.Provisioning;

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
    protected SchemeMapperBuilder SchemeMapperBuilder { get; }

    public DubUrlProvisionerBuilder()
        : this(new()) { }

    protected internal DubUrlProvisionerBuilder(SchemeMapperBuilder schemeMapperBuilder)
        => SchemeMapperBuilder = schemeMapperBuilder;

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

    protected IPackageProvisioner Build()
    {
        if (DatabaseType is null)
            throw new InvalidOperationException("Database type must be specified.");
        if (string.IsNullOrEmpty(Server) && InMemory == false)
            throw new InvalidOperationException("Server must be specified or in-memry should be set to true.");

        var server = InMemory ? "memory" : Server;
        var credentials = Username is null || Password is null ? string.Empty : $"{Username}:{Password}@";
        var port = Port is null ? string.Empty : $":{Port}";
        var database = Database is null ? string.Empty : $"/{Database}";

        var url = $"{DatabaseType}://{credentials}{server}{port}{database}";

        return new DubUrlProvisioner(new ConnectionUrl(url, SchemeMapperBuilder));
    }

    IPackageProvisioner IPackageProvisionerBuilder.Build()
        => Build();
}
