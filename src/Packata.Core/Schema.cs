using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class Schema
{
    public string? Profile { get; set; }
    public List<Field> Fields { get; set; } = [];
}
