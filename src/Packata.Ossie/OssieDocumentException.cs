namespace Packata.Ossie;

/// <summary>Reports invalid or unsupported Ossie documents.</summary>
public sealed class OssieDocumentException : FormatException
{
    internal OssieDocumentException(string path, string message, Exception? innerException = null)
        : base($"{path}: {message}", innerException)
    {
        Path = path;
    }

    /// <summary>Gets the document path, expressed as $ followed by a JSON Pointer.</summary>
    /// <value>The location of the invalid declaration.</value>
    public string Path { get; }
}
