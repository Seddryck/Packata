namespace Packata.ResourceReaders;

/// <summary>Contributes native field bindings to a reader selected by its binding key.</summary>
/// <remarks>Implementations must support concurrent calls and return configuration for an individual opening.</remarks>
public interface IReaderBindingProvider
{
    /// <summary>Returns a binding for the requested type and encoding, or null when the reader cannot produce it.</summary>
    /// <remarks>Apply the request's missing-value rules before parsing when the reader does not expose decoded text.
    /// Return values that remain usable after reader advancement and disposal.</remarks>
    ReaderFieldBinding? TryBind(ReaderRepresentationRequest request);
}
