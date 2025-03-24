using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Provisioning;
public interface IPackageProvisioner
{
    void Execute(DataPackage dataPackage);

    void DeploySchema(DataPackage dataPackage);
    void LoadData(DataPackage dataPackage);

    void DeploySchema(Resource resource);
    void LoadData(Resource resource);
}
