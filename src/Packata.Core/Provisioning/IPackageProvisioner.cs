using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Provisioning;
public interface IPackageProvisioner
{
    void Execute(DataPackage dataPackage, ProvisionerOptions options);

    void DeploySchema(DataPackage dataPackage, ProvisionerOptions options);
    void LoadData(DataPackage dataPackage);

    void DeploySchema(Resource resource, ProvisionerOptions options);
    void LoadData(Resource resource);
}
