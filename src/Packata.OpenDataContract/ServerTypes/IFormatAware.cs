using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public interface IFormatAware
{
    /// <summary>
    /// The format of the file(s)
    /// </summary>
    [Label("Format")]
    public string Format { get; set; }
}

