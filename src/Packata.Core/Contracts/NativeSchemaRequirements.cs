namespace Packata.Core.Contracts;

/// <summary>Structural restrictions retained in source metadata that the two booleans cannot express.</summary>
internal static class NativeSchemaRequirements
{
    internal static bool RequiresOverlap(DataSchema schema)
        => schema.Extensions.TryGetNamespace("datapackage", out var native)
            && native.GetValueOrDefault("fieldsMatch") is "Partial";
}
