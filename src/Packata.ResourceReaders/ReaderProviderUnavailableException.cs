namespace Packata.ResourceReaders;

/// <summary>Indicates that a recognized format has no registered reader provider.</summary>
public sealed class ReaderProviderUnavailableException : NotSupportedException
{
    public ReaderProviderUnavailableException(KnownFormat knownFormat)
        : base($"Resource format '{knownFormat.Name}' requires package '{knownFormat.PackageName}' and " +
               $"registration with '{knownFormat.RegistrationMethod}'.")
    {
        KnownFormat = knownFormat;
    }

    public KnownFormat KnownFormat { get; }
    public string Format => KnownFormat.Name;
    public string SuggestedPackage => KnownFormat.PackageName;
    public string RegistrationMethod => KnownFormat.RegistrationMethod;
}
