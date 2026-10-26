namespace Packata.ResourceReaders;

/// <summary>A category-table reference or its lookup rows cannot be used for label decoding.</summary>
public sealed class CategoryTableDecodingException : InvalidOperationException
{
    internal CategoryTableDecodingException(string resource, string reason)
        : base($"Category table '{resource}': {reason}.") { }
}
