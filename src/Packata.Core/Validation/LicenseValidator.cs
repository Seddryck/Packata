using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class LicenseValidator : IValidator<License>
{
    protected virtual string NameRegex { get => DefaultRegex.UrlableRegex; }
    protected virtual string PathRegex { get => DefaultRegex.PathRegex; }

    public bool IsValid(License license)
        => HasRequiredFields(license, out var _) && IsNameValid(license, out var _) && IsPathValid(license, out var _);

    protected virtual bool HasRequiredFields(License license, out Exception? exception)
    {
        exception = license.Name is null && license.Path is null
            ? new ArgumentNullException("The properties 'name' and 'path' cannot be null at the same time.")
            : null;
        return exception is null;
    }

    protected virtual bool IsNameValid(License license, out Exception? exception)
    {
        exception = license.Name is not null && !Regex.IsMatch(license.Name, NameRegex)
            ? new ArgumentOutOfRangeException($"The property 'name' doesn't validate the regex '{NameRegex}'.")
            : null;
        return exception is null;
    }

    protected virtual bool IsPathValid(License license, out Exception? exception)
    {
        exception = license.Path is not null && !Regex.IsMatch(license.Path, PathRegex)
            ? new ArgumentOutOfRangeException($"The property 'path' doesn't validate the regex '{PathRegex}'.")
            : null;
        return exception is null;
    }
}
