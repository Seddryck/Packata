using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Provisioning;
public interface IPackageProvisionerBuilder
{
    IPackageProvisioner Build();
}
