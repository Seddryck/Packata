using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal static class ReaderMissingValues
{
    internal static IReadOnlyList<string> Resolve(DataSchema schema, DataField field)
    {
        if (Read(field.Extensions) is { } values) return Array.AsReadOnly(values.ToArray());
        if (Read(schema.Extensions) is { } schemaValues) return Array.AsReadOnly(schemaValues.ToArray());
        return schema.Extensions.Namespaces.Contains("datapackage")
               && !string.Equals(field.LogicalType?.Trim(), "string", StringComparison.OrdinalIgnoreCase)
            ? [string.Empty]
            : [];
    }

    private static IEnumerable<string>? Read(ExtensionMetadata extensions) =>
        extensions.TryGetNamespace("datapackage", out var metadata)
        && metadata.TryGetValue("missingValues", out var raw)
            ? raw as IEnumerable<string>
            : null;
}
