using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners;
public static class DataPackageExtensions
{
    public static void Provision(this DataPackage dataPackage, Func<DubUrlProvisionerBuilder, IPackageProvisionerBuilder> provision, ProvisionerOptions? options = null)
    {
        var provisioner = provision(new()).Build();
        provisioner.Execute(dataPackage, options ?? new());
    }
}
