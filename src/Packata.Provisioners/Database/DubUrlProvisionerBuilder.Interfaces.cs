using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Provisioning;

namespace Packata.Provisioners.Database;
public interface IDubUrlTypeStep
{
    IDubUrlLocationStep Using(string databaseType);
}

public interface IDubUrlLocationStep
{
    IDubUrlSettingsStep OnServer(string server);
    IDubUrlSettingsStep InMemory();
    IDubUrlSettingsStep Local();
}

public interface IDubUrlSettingsStep : IPackageProvisionerBuilder
{
    IDubUrlSettingsStep WithCredentials(string username, string password);
    IDubUrlSettingsStep WithDatabase(string dbname);
    IDubUrlSettingsStep WithPort(int port);
}
