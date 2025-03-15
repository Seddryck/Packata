using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders;
public static class ResourceExtensions
{
    /// <summary>
    /// Returns a IDataReader for the resource.
    /// </summary>
    /// <returns>An IDataReader</returns>
    public static IDataReader ToDataReader(this Resource resource)
    {
        var factory = new ResourceReaderFactory();
        var reader = factory.Create(resource);
        return reader.ToDataReader(resource);
    }
}
