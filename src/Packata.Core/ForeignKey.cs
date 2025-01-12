using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class ForeignKey
{
    public List<string> Fields { get; set; } = [];
    public Reference? Reference { get; set; } = null;
}
