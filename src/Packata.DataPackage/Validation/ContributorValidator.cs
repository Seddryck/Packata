using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Packata.Core.Validation;
public class ContributorValidator : IValidator<Contributor>
{
    protected virtual string PathRegex { get => DefaultRegex.PathRegex; }
    protected virtual string EmailRegex { get => DefaultRegex.EmailRegex; }

    public bool IsValid(Contributor contributor)
        => AtLeastOnePropertySet(contributor, out var _) && IsEmailValid(contributor, out var _) && IsPathValid(contributor, out var _);

    protected virtual bool AtLeastOnePropertySet(Contributor contributor, out Exception? exception)
    {
        exception = contributor.Email is null
                        && contributor.Path is null
                        && contributor.GivenName is null
                        && contributor.FamilyName is null
                        && contributor.Title is null
                        && contributor.Organization is null
            ? new ArgumentOutOfRangeException($"All properties are not set. At least one of them must be set.")
            : null;
        return exception is null;
    }

    protected virtual bool IsEmailValid(Contributor contributor, out Exception? exception)
    {
        exception = contributor.Email is not null && !Regex.IsMatch(contributor.Email, EmailRegex)
            ? new ArgumentOutOfRangeException($"The property 'email' doesn't validate the regex '{EmailRegex}'.")
            : null;
        return exception is null;
    }

    protected virtual bool IsPathValid(Contributor contributor, out Exception? exception)
    {
        exception = contributor.Path is not null && !Regex.IsMatch(contributor.Path, PathRegex)
            ? new ArgumentOutOfRangeException($"The property 'path' doesn't validate the regex '{PathRegex}'.")
            : null;
        return exception is null;
    }
}
