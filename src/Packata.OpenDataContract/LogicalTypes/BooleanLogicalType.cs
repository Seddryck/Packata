using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract;

/// <summary>
/// Additional metadata options for a logical type "array".
/// </summary>
public class BooleanLogicalType : ILogicalType
{
    public BooleanLogicalType(Dictionary<string, object>? dict)
    {
        if (dict is null)
            return;
    }
}
