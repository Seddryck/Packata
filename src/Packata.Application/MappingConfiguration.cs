using System.Text.Json;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract.Mapping;

namespace Packata.Application;

internal static class MappingConfiguration
{
    private const string OdcsPrefix = "mapping.odcs.";
    private const string DefaultValueKey = "mapping.datapackage.field.defaultValue";
    private const string DefaultExpressionKey = "mapping.datapackage.field.defaultExpression";
    private const string PhysicalTypeContextKey = "mapping.datapackage.field.physicalTypeContext";
    private static readonly string[] Keys =
    [
        OdcsPrefix + "schema.allowAdditionalProperties",
        OdcsPrefix + "schema.enforcePropertyOrder",
        OdcsPrefix + "schema.categoryFieldMap",
        OdcsPrefix + "field.categories",
        OdcsPrefix + "field.defaultValue",
        OdcsPrefix + "field.defaultExpression",
        OdcsPrefix + "field.physicalTypeContext",
        OdcsPrefix + "server.delimiter",
        OdcsPrefix + "server.quoteChar",
        OdcsPrefix + "server.lineTerminator",
        OdcsPrefix + "server.header",
        OdcsPrefix + "server.sheetName",
        OdcsPrefix + "server.sheetNumber",
        OdcsPrefix + "server.headerRows",
        OdcsPrefix + "server.commentRows",
        DefaultValueKey,
        DefaultExpressionKey,
        PhysicalTypeContextKey,
    ];

    internal static IEnumerable<string> SettingKeys => Keys.Select(key => key);

    internal static bool IsMappingKey(string key) => key.StartsWith("mapping.", StringComparison.Ordinal);

    internal static bool Supports(string key) => Keys.Contains(key, StringComparer.Ordinal);

    internal static string Path => UserConfigurationFile.PathFor(Environment.GetEnvironmentVariable);

    internal static ContractMappingOptions Load() => Resolve(UserConfigurationFile.ReadMappings(Path));

    internal static ContractMappingOptions Resolve(IReadOnlyDictionary<string, string> settings)
    {
        foreach (var key in settings.Keys.Where(IsMappingKey))
            if (!Supports(key)) throw new FormatException($"Unknown mapping configuration key '{key}'.");

        var odcs = new OdcsMappingOptions
        {
            Schema = new()
            {
                AllowAdditionalProperties = ReadOdcs(settings, OdcsPrefix + "schema.allowAdditionalProperties"),
                EnforcePropertyOrder = ReadOdcs(settings, OdcsPrefix + "schema.enforcePropertyOrder"),
                CategoryFieldMap = ReadOdcs(settings, OdcsPrefix + "schema.categoryFieldMap"),
            },
            Field = new()
            {
                Categories = ReadOdcs(settings, OdcsPrefix + "field.categories"),
                DefaultValue = ReadOdcs(settings, OdcsPrefix + "field.defaultValue"),
                DefaultExpression = ReadOdcs(settings, OdcsPrefix + "field.defaultExpression"),
                PhysicalTypeContext = ReadOdcs(settings, OdcsPrefix + "field.physicalTypeContext"),
            },
            Server = new()
            {
                Delimiter = ReadOdcs(settings, OdcsPrefix + "server.delimiter"),
                QuoteChar = ReadOdcs(settings, OdcsPrefix + "server.quoteChar"),
                LineTerminator = ReadOdcs(settings, OdcsPrefix + "server.lineTerminator"),
                Header = ReadOdcs(settings, OdcsPrefix + "server.header"),
                SheetName = ReadOdcs(settings, OdcsPrefix + "server.sheetName"),
                SheetNumber = ReadOdcs(settings, OdcsPrefix + "server.sheetNumber"),
                HeaderRows = ReadOdcs(settings, OdcsPrefix + "server.headerRows"),
                CommentRows = ReadOdcs(settings, OdcsPrefix + "server.commentRows"),
            },
        };
        var dataPackage = new DataPackageMappingOptions
        {
            DefaultValue = ReadDataPackage(settings, DefaultValueKey),
            DefaultExpression = ReadDataPackage(settings, DefaultExpressionKey),
            PhysicalTypeContext = ReadDataPackage(settings, PhysicalTypeContextKey),
        };
        try
        {
            _ = new OpenDataContractMapper(odcs);
            _ = new DataPackageMapper(dataPackage);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException(exception.Message, exception);
        }
        return new(odcs, dataPackage);
    }

    internal static (object Effective, object BuiltIn) Describe(string key, IReadOnlyDictionary<string, string> settings)
    {
        var name = key[(key.LastIndexOf('.') + 1)..];
        if (!key.StartsWith(OdcsPrefix, StringComparison.Ordinal)) return (ReadProperty(settings, key) ?? name, name);
        var replacement = ReadOdcs(settings, key);
        return (new { property = replacement.Property ?? name, vendor = replacement.InheritVendor ? "packata" : replacement.Vendor },
            new { property = name, vendor = "packata" });
    }

    private static string? ReadProperty(IReadOnlyDictionary<string, string> settings, string key)
        => settings.TryGetValue(key, out var value) ? RequireText(value, key, "property") : null;

    private static DataPackageBindingOverride ReadDataPackage(IReadOnlyDictionary<string, string> settings, string key)
        => ReadProperty(settings, key) is { } property
            ? DataPackageBindingOverride.FromProperty(property) : DataPackageBindingOverride.BuiltIn;

    private static OdcsBindingOverride ReadOdcs(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value)) return OdcsBindingOverride.BuiltIn;
        try
        {
            using var document = JsonDocument.Parse(value ?? "");
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new FormatException($"'{key}' requires a JSON object with 'property' or 'vendor'.");
            return ReadOdcsSelector(document.RootElement, key);
        }
        catch (JsonException exception)
        {
            throw new FormatException($"'{key}' requires a JSON object with 'property' or 'vendor'.", exception);
        }
    }

    private static OdcsBindingOverride ReadOdcsSelector(JsonElement selector, string key)
    {
        string? property = null;
        string? vendor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in selector.EnumerateObject())
        {
            if (!seen.Add(entry.Name) || entry.Name is not ("property" or "vendor"))
                throw new FormatException($"'{key}' accepts 'property' and 'vendor' at most once each.");
            if (entry.Name == "property") property = ReadText(entry.Value, key, "property");
            else if (entry.Value.ValueKind != JsonValueKind.Null) vendor = ReadText(entry.Value, key, "vendor");
        }
        if (seen.Count == 0) throw new FormatException($"'{key}' requires at least one of 'property' and 'vendor'.");
        if (!seen.Contains("vendor")) return OdcsBindingOverride.UseBuiltInVendor(property);
        return vendor is null ? OdcsBindingOverride.Unvendored(property) : OdcsBindingOverride.ForVendor(vendor, property);
    }

    private static string ReadText(JsonElement value, string key, string member)
        => RequireText(value.ValueKind == JsonValueKind.String ? value.GetString() : null, key, member);

    private static string RequireText(string? value, string key, string member)
        => !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException($"'{key}' requires a non-empty string for '{member}'.");
}
