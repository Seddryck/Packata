using System.Text.Json.Nodes;
using System.Text.Json;
using Json.Schema;

namespace Packata.Documents.Validation;

public sealed record DescriptorProfile(string Uri, string Label, string Format);

/// <summary>Explicit, offline profile selection. Source URIs never trigger downloads.</summary>
public static class DescriptorProfiles
{
    public static string DataPackageUri => Registrations["datapackage"].Profile.Uri;
    public static string CategoryTablesUri => Registrations["category-tables"].Profile.Uri;
    public static string OdcsUri => Registrations["odcs"].Profile.Uri;

    private sealed record Registration(DescriptorProfile Profile, Lazy<JsonSchema> Artifact);
    private sealed record Definition(string Id, string Uri, string Label, string Format, string File, string Dialect);
    private static readonly IReadOnlyDictionary<string, Registration> Registrations = ReadRegistry();

    internal static DescriptorProfile? Select(JsonObject root, bool odcs)
    {
        string? uri = odcs ? OdcsUri : DataPackageUri;
        if (!odcs && root.ContainsKey("$schema")) uri = DescriptorValidation.Text(root["$schema"]);
        return Registrations.Values.FirstOrDefault(item => item.Profile.Uri == uri)?.Profile;
    }

    internal static JsonSchema Schema(DescriptorProfile profile) => Registrations.Values.Single(item => item.Profile == profile).Artifact.Value;

    private static IReadOnlyDictionary<string, Registration> ReadRegistry()
    {
        var definitions = JsonSerializer.Deserialize<Definition[]>(ReadResource("profiles.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The pinned profile registry is missing.");
        return definitions.ToDictionary(item => item.Id, item => new Registration(new(item.Uri, item.Label, item.Format),
            new(() => JsonSchema.FromText(ReadResource(item.File), new BuildOptions { Dialect = item.Dialect switch
            {
                "draft7" => Dialect.Draft07,
                "draft2019-09" => Dialect.Draft201909,
                _ => throw new InvalidOperationException("Unsupported pinned schema dialect.")
            } }))));
    }

    private static string ReadResource(string file)
    {
        using var stream = typeof(DescriptorProfiles).Assembly.GetManifestResourceStream($"Packata.Documents.Validation.Schemas.{file}")
            ?? throw new InvalidOperationException("The pinned profile resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
