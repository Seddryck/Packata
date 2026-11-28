namespace Packata.Core.Reading;

/// <summary>Optional access to a cell before contract conversion. A missing cell is returned as null.</summary>
public interface IRawDataRecord
{
    object? GetRawValue(int ordinal);
}
