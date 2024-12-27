using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.ResourceReading;

namespace Packata.Core;

public partial class Resource
{
    /// <summary>
    /// Returns a IDataReader for the resource.
    /// </summary>
    /// <returns>An IDataReader</returns>
    public IDataReader ToDataReader()
    {
        var factory = new ResourceReaderFactory();
        var reader = factory.Create(this);
        return reader.ToDataReader(this);
    }
}
