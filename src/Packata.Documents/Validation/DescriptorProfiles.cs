using System.Text.Json.Nodes;
using Json.Schema;

namespace Packata.Documents.Validation;

public sealed record DescriptorProfile(string Uri, string Label, string Format);

/// <summary>Explicit, offline profile selection. Source URIs never trigger downloads.</summary>
public static class DescriptorProfiles
{
    public const string DataPackageUri = "https://datapackage.org/profiles/2.0/datapackage.json";
    public const string CategoryTablesUri = "https://raw.githubusercontent.com/Seddryck/Packata/profile-category-tables-v1.0.0/docs/profiles/category-tables/1.0.0/datapackage.json";
    public const string OdcsUri = "https://raw.githubusercontent.com/bitol-io/open-data-contract-standard/f0bdad95346905d500be5ef4b2c2d9b1d95223b7/schema/odcs-json-schema-v3.2.0.json";

    private sealed record Registration(DescriptorProfile Profile, Lazy<JsonSchema> Schema);
    private static readonly Registration[] Registrations =
    [
        Register(DataPackageUri, "Data Package v2.0", "datapackage", "datapackage.json", Dialect.Draft07),
        Register(CategoryTablesUri, "Packata Category Tables 1.0.0", "datapackage", "category-tables-v1.0.0.json", Dialect.Draft07),
        Register(OdcsUri, "ODCS v3.2.0", "odcs", "odcs-v3.2.0.json", Dialect.Draft201909)
    ];

    internal static DescriptorProfile? Select(JsonObject root, bool odcs)
    {
        string? uri = odcs ? OdcsUri : DataPackageUri;
        if (!odcs && root.ContainsKey("$schema")) uri = DescriptorValidation.Text(root["$schema"]);
        return Registrations.FirstOrDefault(item => item.Profile.Uri == uri)?.Profile;
    }

    internal static JsonSchema Schema(DescriptorProfile profile) => Registrations.Single(item => item.Profile == profile).Schema.Value;

    private static Registration Register(string uri, string label, string format, string file, Dialect dialect)
        => new(new(uri, label, format), new(() => Load(file, dialect)));

    private static JsonSchema Load(string file, Dialect dialect)
    {
        using var stream = typeof(DescriptorProfiles).Assembly.GetManifestResourceStream($"Packata.Documents.Validation.Schemas.{file}")
            ?? throw new InvalidOperationException("The pinned profile resource is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd(), new BuildOptions { Dialect = dialect });
    }
}
