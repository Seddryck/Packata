namespace Packata.ResourceReaders;

/// <summary>Describes a format recognized by Packata whose implementation is supplied separately.</summary>
public sealed record KnownFormat(
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> MediaTypes,
    string PackageName,
    string RegistrationMethod);
