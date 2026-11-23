using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal sealed record ReaderRepresentationProfile(string Name, Func<DataField, Type?> SelectType)
{
    internal static (string ProfileName, Type Type)? Resolve(
        IReadOnlyList<ReaderRepresentationProfile> profiles, DataField field)
    {
        (string ProfileName, Type Type)? selected = null;
        foreach (var profile in profiles)
        {
            if (profile.SelectType(field) is not { } type) continue;
            if (selected is { } previous)
            {
                throw new InvalidOperationException(
                    $"Reader representation profiles '{previous.ProfileName}' and '{profile.Name}' both select a type for field '{field.Name}'.");
            }
            selected = (profile.Name, type);
        }

        return selected;
    }
}
