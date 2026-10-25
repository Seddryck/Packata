using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Packata.Core.Contracts;

/// <summary>Creates detached, read-only snapshots of format-specific descriptive metadata.</summary>
public static class MetadataSnapshot
{
    public static IReadOnlyDictionary<string, object?> CopyDictionary(IEnumerable<KeyValuePair<string, object?>> values)
        => new ReadOnlyDictionary<string, object?>(values.ToDictionary(pair => pair.Key, pair => CopyValue(pair.Value), StringComparer.Ordinal));

    public static object? CopyValue(object? value) => value switch
    {
        null => null,
        JsonElement element => element.Clone(),
        string or char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or Guid => value,
        IReadOnlyDictionary<string, object?> dictionary => CopyDictionary(dictionary),
        IDictionary dictionary => new ReadOnlyDictionary<string, object?>(dictionary.Keys.Cast<object>()
            .ToDictionary(key => Convert.ToString(key)!, key => CopyValue(dictionary[key]), StringComparer.Ordinal)),
        IEnumerable sequence => Array.AsReadOnly(sequence.Cast<object?>().Select(CopyValue).ToArray()),
        _ => JsonSerializer.SerializeToElement(value).Clone()
    };

    public static ExtensionMetadata CopyExtensions(ExtensionMetadata? metadata)
        => metadata is null ? ExtensionMetadata.Empty : new ExtensionMetadata(metadata.Namespaces.ToDictionary(
            name => name, name => CopyDictionary(metadata[name]), StringComparer.Ordinal));
}
