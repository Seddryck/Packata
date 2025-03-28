using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Provisioning;

public record ProvisionerOptions
(
    ConstraintsOptions Constraints = ConstraintsOptions.AllConstraints
)
{ }
