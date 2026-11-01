namespace Packata.Core.Reading;

/// <summary>Optional reader metadata preserving source header identity independently of current row width.</summary>
public interface IOriginalColumnNames
{
    /// <summary>Original decoded headers after reader initialization, or null when unavailable.</summary>
    /// <value>
    /// Original decoded headers after reader initialization, or null when unavailable.
    /// </value>
    IReadOnlyList<string>? OriginalColumnNames { get; }
}
