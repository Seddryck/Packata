namespace Packata.ResourceReaders;

/// <summary>Category decoding was requested for an engine that does not support it.</summary>
public sealed class CategoryDecodingNotSupportedException : NotSupportedException
{
    public CategoryDecodingNotSupportedException()
        : base("Category decoding (--decode-categories) is supported only by the PocketCsvReader delimited engine (CSV, TSV, PSV).") { }
}
