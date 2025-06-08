using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public interface ICatalogAware
{
    /// <summary>
    /// The name of the catalog.
    /// </summary>
    [Label("Catalog")]
    public string Catalog { get; set; }
}

