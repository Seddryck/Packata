using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
/// <summary>
/// Interface for strategies that infer table dialect information from a resource.
/// </summary>
public interface IDialectInference
{
    /// <summary>
    /// Attempts to infer a table dialect from the given resource.
    /// </summary>
    /// <param name="resource">The resource to infer from.</param>
    /// <param name="dialect">When this method returns, contains the inferred dialect if successful, or null if unsuccessful.</param>
    /// <returns>true if a dialect was successfully inferred; otherwise, false.</returns>
    bool TryInfer(Resource resource, out TableDelimitedDialect? dialect);
}
