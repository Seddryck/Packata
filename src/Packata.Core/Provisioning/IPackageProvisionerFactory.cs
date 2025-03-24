using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Provisioning;
public interface IPackageProvisionerFactory
{
    void AddOrReplaceProvisioner(string scheme);
    IPackageProvisioner Create(Resource resource);
}
