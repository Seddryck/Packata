namespace Packata.DataPackage;

public interface ICategory
{
    object Value { get; }
    string? Label { get; }
}

/// <summary>An object-form category with an optional display label.</summary>
public class Category : ICategory
{
    public object Value { get; }
    public string? Label { get; }
    public Category(object value, string? label = null)
        => (Value, Label) = (CategoryValues.Normalize(value), label);
}

/// <summary>A scalar string category. Label remains available for existing callers.</summary>
public class CategoryLabel(string label) : ICategory
{
    public object Value => Label;
    public string Label { get; } = label;
}

/// <summary>A scalar integer category.</summary>
public class CategoryInteger(long value) : ICategory
{
    public object Value { get; } = value;
    public string? Label => null;
}

internal static class CategoryValues
{
    public static object Normalize(object value) => value switch
    {
        string text => text,
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value),
        ulong number when number <= long.MaxValue => (long)number,
        _ => throw new ArgumentException("A category value must be a string or a signed 64-bit integer.", nameof(value))
    };
    public static bool TryNormalize(object? value, out object? normalized)
    {
        try { normalized = Normalize(value!); return true; }
        catch (ArgumentException) { normalized = null; return false; }
    }
}
