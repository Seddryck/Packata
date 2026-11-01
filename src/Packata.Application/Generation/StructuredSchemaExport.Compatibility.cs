using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

internal sealed partial class StructuredSchemaExport
{
    private void ValidateCompatibility(DataAsset asset, IReadOnlyList<Field> fields)
    {
        if (profile is null) return;
        if (!SchemaCompatibilityProfiles.Supports(profile, outputFormat))
        {
            ProfileError(asset.Id, "Unknown or incompatible export profile.");
            return;
        }
        if (profile == "hudi" && !AvroIdentifier(options?.Name ?? asset.Name))
            ProfileError(asset.Id, "Hudi profile requires an explicit valid Avro record name; use --schema-name.");
        if (profile == "delta")
        {
            ValidateDeltaMetadata(asset.Extensions, asset.Id);
            ValidateDeltaMetadata(asset.Schema!.Extensions, asset.Id + "/schema");
        }
        ValidateProfileFields(fields);
    }

    private void ValidateProfileFields(IReadOnlyList<Field> fields)
    {
        var names = new HashSet<string>(profile == "delta" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var field in fields)
        {
            token.ThrowIfCancellationRequested();
            if (!names.Add(field.Name)) ProfileError(field.Path, "Field names collide under the selected profile's name comparison.");
            if (profile == "delta" && (field.Name.Length == 0 || field.Name.IndexOfAny(" ,;{}()\n\t=".ToCharArray()) >= 0))
                ProfileError(field.Path, "Delta without column mapping forbids spaces and characters ,;{}()\n\t= in field names. Rename the field.");
            if (profile == "hudi") ValidateHudiField(field);
            if (profile == "delta") ValidateDeltaMetadata(field.Source.Extensions, field.Path);
            ValidateProfileFields(field.Children);
            if (field.Items is not null) ValidateProfileElement(field.Items);
            if (field.MapKey is not null) ValidateProfileElement(field.MapKey);
            if (field.MapValue is not null) ValidateProfileElement(field.MapValue);
        }
    }

    // Collection element labels are not native struct field names.
    private void ValidateProfileElement(Field field)
    {
        if (profile == "hudi") ValidateHudiField(field, named: false);
        if (profile == "delta") ValidateDeltaMetadata(field.Source.Extensions, field.Path);
        ValidateProfileFields(field.Children);
        if (field.Items is not null) ValidateProfileElement(field.Items);
        if (field.MapKey is not null) ValidateProfileElement(field.MapKey);
        if (field.MapValue is not null) ValidateProfileElement(field.MapValue);
    }

    private void ValidateDeltaMetadata(ExtensionMetadata metadata, string path)
    {
        foreach (var entry in TargetMetadata.Entries(metadata))
        {
            if (entry.Key.StartsWith("delta.", StringComparison.OrdinalIgnoreCase) || entry.Key.StartsWith("packata.delta.", StringComparison.OrdinalIgnoreCase))
                ProfileError(path, "Reserved Delta metadata requires explicit table features or an existing-table baseline and is unsupported by this new-schema profile.");
        }
    }

    private void ValidateHudiField(Field field, bool named = true)
    {
        if (named && !AvroIdentifier(field.Name)) ProfileError(field.Path, "Hudi profile requires valid Avro identifiers without name normalization. Rename the field.");
        if (named && field.Name.StartsWith("_hoodie_", StringComparison.OrdinalIgnoreCase))
            ProfileError(field.Path, "Hudi-managed metadata columns must not be supplied as user fields.");
        if (field.Kind == "time" || (field.Parameters.TemporalSemantics == TemporalSemantics.Local && field.Kind is "timestamp" or "datetime"))
            ProfileError(field.Path, "This Hudi/Spark baseline does not support time-only or local-timestamp logical types. Use an instant timestamp or an explicitly modeled string.");
        if (field.Kind == "decimal" && field.Parameters.Precision > 38)
            ProfileError(field.Path, "The Hudi Spark engine baseline supports decimal precision at most 38.");
        if (field.EnumSymbols is not null)
            ProfileError(field.Path, "This Hudi Spark profile excludes Avro enums; use a string and validate membership separately.");
        if (field.Source.Constraints.Any(c => c.Kind.Equals("default", StringComparison.OrdinalIgnoreCase)))
            ProfileError(field.Path, "Explicit defaults are not emitted by the shared Avro serializer. Remove the default declaration or validate it separately; no default is inferred.");
    }

    private static bool AvroIdentifier(string value) => Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private void ProfileError(string path, string message) => diagnostics.Add(new("SCHEMA_PROFILE", "Error", path, message, profile));
}

/// <summary>Reads canonical target namespaces or named ODCS custom properties; never searches arbitrary payloads.</summary>
internal static class TargetMetadata
{
    internal static IEnumerable<KeyValuePair<string, JToken>> Entries(ExtensionMetadata metadata)
    {
        foreach (var ns in metadata.Namespaces)
        {
            foreach (var pair in metadata[ns])
            {
                if (ns == "odcs" && pair.Key == "customProperties" && pair.Value is not null && JToken.FromObject(pair.Value) is JArray properties)
                {
                    foreach (var property in properties)
                    {
                        if (property["property"]?.Value<string>() is { } name)
                            yield return new(name, property["value"] ?? JValue.CreateNull());
                    }
                }
                else
                {
                    yield return new(ns + "." + pair.Key, pair.Value is null ? JValue.CreateNull() : JToken.FromObject(pair.Value));
                }
            }
        }
    }

    internal static JToken? Value(ExtensionMetadata metadata, string target, string key)
    {
        var matches = Entries(metadata).Where(p => p.Key == target + "." + key || p.Key == "packata." + target + "." + key).ToArray();
        return matches.Length == 0 ? null : matches.Length == 1 ? matches[0].Value : new JArray(matches.Select(p => p.Value));
    }
}
