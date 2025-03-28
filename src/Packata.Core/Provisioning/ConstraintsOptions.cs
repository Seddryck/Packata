using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Provisioning;

[Flags]
public enum ConstraintsOptions
{
    PrimaryKey = 1,
    Unique = 2,
    Required = 4,
    Checks = 8,
    AllConstraints = PrimaryKey | Unique | Required | Checks
}
