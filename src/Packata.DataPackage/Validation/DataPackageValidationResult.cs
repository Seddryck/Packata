namespace Packata.DataPackage.Validation;

public sealed record DataPackageValidationIssue(string Path, string Message);

public sealed class DataPackageValidationResult(IReadOnlyList<DataPackageValidationIssue> issues)
{
    public IReadOnlyList<DataPackageValidationIssue> Issues { get; } = issues;
    public bool IsValid => Issues.Count == 0;
}
