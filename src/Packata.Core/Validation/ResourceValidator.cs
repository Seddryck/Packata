using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class ResourceValidator : IValidator<Resource>
{
    protected virtual string PathRegex { get => DefaultRegex.PathRegex; }

    public bool IsValid(Resource resource)
        => IsNameSet(resource, out var _) && IsSinglePropertySet(resource, out var _)
            && IsPathValid(resource, out var _) && IsPathCoherent(resource, out var _);

    protected internal virtual bool IsSinglePropertySet(Resource resource, out Exception? exception)
    {
        exception = (resource.Paths.Count() > 0 && resource.Data is not null)
                    || (resource.Paths.Count() == 0 && resource.Data is null)
            ? new ArgumentOutOfRangeException($"Properties 'path' and 'data' cannot be simultaneously set. At least one of them must be null.")
            : null;
        return exception is null;
    }

    protected virtual bool IsNameSet(Resource Resource, out Exception? exception)
    {
        exception = Resource.Name is null
            ? new ArgumentNullException($"The property 'name' must be set.")
            : null;
        return exception is null;
    }

    protected virtual bool IsPathValid(Resource Resource, out Exception? exception)
    {
        exception = Resource.Paths is not null && Resource.Paths.Any(r => !Regex.IsMatch(r.ToString()!, PathRegex))
            ? new ArgumentOutOfRangeException($"At least one member of the property 'path' doesn't validate the regex '{PathRegex}'.")
            : null;
        return exception is null;
    }

    protected virtual bool IsPathCoherent(Resource Resource, out Exception? exception)
    {
        exception = (Resource.Paths.Count() > 0) && (Resource.Paths.Any(r => r.ToString()!.Contains("://")) ^ Resource.Paths.All(r => r.ToString()!.Contains("://")))
            ? new ArgumentOutOfRangeException($"It is not permitted to mix fully qualified URLs and relative paths in a path array. Values MUST either all be relative paths or all URLs.")
            : null;
        return exception is null;
    }
}
