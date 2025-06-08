using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract.Types;

public class UnknownLogicalType : ILogicalType
{
    public UnknownLogicalType(string type)
    {
        Type = type;
    }
    public string Type { get; set; }
}
