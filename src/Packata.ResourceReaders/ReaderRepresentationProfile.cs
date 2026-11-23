using Packata.Core.Contracts;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

internal sealed record ReaderRepresentationProfile(
    string Name,
    Func<DataField, Type?> SelectType,
    DelimitedRepresentationBinding Delimited)
{
    internal static (ReaderRepresentationProfile Profile, Type Type)? Resolve(
        IReadOnlyList<ReaderRepresentationProfile> profiles, DataField field)
    {
        (ReaderRepresentationProfile Profile, Type Type)? selected = null;
        foreach (var profile in profiles)
        {
            if (profile.SelectType(field) is not { } type) continue;
            if (selected is { } previous)
            {
                throw new InvalidOperationException(
                    $"Reader representation profiles '{previous.Profile.Name}' and '{profile.Name}' both select a type for field '{field.Name}'.");
            }
            selected = (profile, type);
        }

        return selected;
    }
}

internal sealed record DelimitedRepresentationBinding(
    Action<RuntimeParsersDescriptorBuilder> ConfigureParsers,
    Action<ISchemaDescriptorBuilder, DataField, Type> BindField);
