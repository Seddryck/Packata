using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public enum FieldsMatching
{
    /// <summary>
    /// The data source MUST have exactly the same fields as defined in the fields array. Fields MUST be mapped by their order.
    /// </summary>
    Exact,
    /// <summary>
    /// The data source MUST have exactly the same fields as defined in the fields array. Fields MUST be mapped by their names.
    /// </summary>
    Equal,
    /// <summary>
    /// The data source MUST have all the fields defined in the fields array, but MAY have more. Fields MUST be mapped by their names.
    /// </summary>
    Subset,
    /// <summary>
    /// The data source MUST only have fields defined in the fields array, but MAY have fewer. Fields MUST be mapped by their names.
    /// </summary>
    Superset,
    /// <summary>
    /// The data source MUST have at least one field defined in the fields array. Fields MUST be mapped by their names.
    /// </summary>
    Partial
}
