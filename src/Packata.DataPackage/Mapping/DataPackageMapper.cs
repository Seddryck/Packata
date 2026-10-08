using System.Collections;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
using Native = Packata.DataPackage;

namespace Packata.DataPackage.Mapping;

/// <summary>Maps the native Data Package v2 model to Packata's standards-neutral model.</summary>
public sealed class DataPackageMapper : IDataContractMapper<Native.DataPackage>
{
    public MappingResult<DataContract> Map(Native.DataPackage document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<MappingDiagnostic>();
        var assets = new List<DataAsset>();
        var endpoints = new List<DataEndpoint>();

        for (var index = 0; index < document.Resources.Count; index++)
        {
            var resource = document.Resources[index];
            var sourcePath = $"$.resources[{index}]";
            var assetId = resource.Name;
            if (string.IsNullOrWhiteSpace(assetId))
            {
                assetId = $"resource-{index + 1}";
                diagnostics.Add(new("DP001", MappingSeverity.Warning, $"{sourcePath}.name",
                    $"The resource has no name; '{assetId}' was generated."));
            }

            var bindings = new List<EndpointBinding>();
            var endpoint = MapEndpoint(resource, assetId, sourcePath, diagnostics);
            if (endpoint is not null)
            {
                endpoints.Add(endpoint);
                bindings.Add(new EndpointBinding(endpoint.Id));
            }

            assets.Add(new DataAsset(
                assetId, resource.Title ?? assetId,
                resource.Dialect?.Table ?? resource.Name,
                resource.Description, MapAssetKind(resource),
                MapSchema(resource.Schema, assetId, document, sourcePath, diagnostics), bindings,
                Extensions: ExtensionMetadata.For("datapackage", ResourceExtensions(resource))));
        }

        var id = document.Id ?? document.Name;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "data-package";
            diagnostics.Add(new("DP002", MappingSeverity.Warning, "$.name",
                "The package has no id or name; 'data-package' was generated."));
        }

        var contract = new DataContract(
            new ContractIdentity(id, document.Name, document.Version),
            new ContractMetadata(document.Title, document.Description, Tags: document.Keywords),
            assets, endpoints,
            new ContractGovernance(
                Ownership: MapOwnership(document.Contributors),
                Terms: document.Licenses.Count == 0 ? null : new TermsOfUse(
                    string.Join(", ", document.Licenses.Select(x => x.Name ?? x.Title ?? x.Path))),
                References: MapReferences(document)),
            ExtensionMetadata.For("datapackage", new Dictionary<string, object?>
            {
                ["profile"] = document.Profile,
                ["created"] = document.Created == default ? null : document.Created,
                ["image"] = document.Image,
                ["sources"] = document.Sources
            }));
        return new MappingResult<DataContract>(contract, diagnostics);
    }

    private static DataEndpoint? MapEndpoint(Native.Resource resource, string assetId,
        string sourcePath, ICollection<MappingDiagnostic> diagnostics)
    {
        DataLocation location;
        EndpointKind kind;
        if (resource.Connection is not null)
        {
            var url = resource.Connection.ConnectionUrl;
            var parsed = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
            var database = resource.Dialect as Native.TableDatabaseDialect;
            location = new ConnectionLocation(parsed?.Scheme ?? "unknown", parsed?.Host,
                parsed is { IsDefaultPort: false } ? parsed.Port : null,
                parsed?.AbsolutePath.Trim('/'), database?.Namespace, url);
            kind = EndpointKind.Database;
        }
        else if (resource.Paths.Count > 0)
        {
            location = new PathLocation(resource.Paths.Select(path => path.Value).ToArray());
            kind = EndpointKind.File;
        }
        else if (resource.Data is not null)
        {
            location = new InlineLocation(NormalizeInlineValue(resource.Data));
            kind = EndpointKind.Inline;
        }
        else
        {
            diagnostics.Add(new("DP003", MappingSeverity.Warning, sourcePath,
                "The resource has no path, connection, or inline data and has no canonical endpoint."));
            return null;
        }

        return new DataEndpoint($"{assetId}-endpoint", resource.Title ?? resource.Name, kind, null, location,
            new DataFormat(resource.Format, resource.MediaType, resource.Encoding, resource.Compression,
                DialectOptions(resource.Dialect)),
            ExtensionMetadata.For("datapackage", new Dictionary<string, object?>
            {
                ["kind"] = resource.Kind, ["profile"] = resource.Profile,
                ["bytes"] = resource.Bytes, ["hash"] = resource.Hash
            }));
    }

    private static DataSchema? MapSchema(Native.Schema? schema, string assetId, Native.DataPackage package,
        string sourcePath, ICollection<MappingDiagnostic> diagnostics)
    {
        if (schema is null) return null;
        var fields = schema.Fields.Select((field, index) =>
        {
            var name = field.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"field-{index + 1}";
                diagnostics.Add(new("DP004", MappingSeverity.Warning,
                    $"{sourcePath}.schema.fields[{index}].name", $"The field has no name; '{name}' was generated."));
            }
            var constraints = field.Constraints is null ? [] : Enumerable.Range(0, field.Constraints.Count)
                .Select(index => field.Constraints[index]).Select(MapConstraint)
                .Where(x => x is not null).Cast<DataConstraint>().ToArray();
            if (field.Categories is { Count: > 0 } && !constraints.Any(value => value.Kind == "enum"))
                constraints = [.. constraints, new DataConstraint("enum", field.Categories.Select(CategoryValue).ToArray())];
            return new DataField(name, field.Type, Format: field.Format,
                Required: field.Constraints?.Get<Native.RequiredConstraint>()?.Value == true,
                Constraints: constraints,
                Extensions: ExtensionMetadata.For("datapackage", FieldExtensions(field)),
                PhysicalName: field.PhysicalName ?? name,
                Description: field.Description);
        }).ToArray();

        var relationships = (schema.ForeignKeys ?? []).Select(foreignKey =>
        {
            var target = foreignKey.Reference?.Resource;
            if (string.IsNullOrWhiteSpace(target)) target = assetId;
            else if (!package.Resources.Any(resource => resource.Name == target))
                diagnostics.Add(new("DP005", MappingSeverity.Warning, $"{sourcePath}.schema.foreignKeys",
                    $"Foreign key target resource '{target}' is not present in the package."));
            return new DataRelationship(foreignKey.Fields, target, foreignKey.Reference?.Fields ?? []);
        }).ToArray();

        return new DataSchema(fields, schema.PrimaryKey, relationships,
            ExtensionMetadata.For("datapackage", new Dictionary<string, object?>
            {
                ["profile"] = schema.Profile, ["fieldsMatch"] = schema.FieldsMatch.ToString(),
                ["uniqueKeys"] = schema.UniqueKeys, ["missingValues"] = schema.MissingValues,
                ["metrics"] = schema.Metrics
            }));
    }

    private static object CategoryValue(Native.ICategory category) => category switch
    {
        Native.Category value => value.Value,
        Native.CategoryLabel value => value.Label,
        _ => category.Label
    };

    private static DataConstraint? MapConstraint(Native.Constraint constraint) => constraint switch
    {
        Native.RequiredConstraint => null,
        Native.UniqueConstraint x => new("unique", x.Value),
        Native.MinLengthConstraint x => new("minLength", x.Value),
        Native.MaxLengthConstraint x => new("maxLength", x.Value),
        Native.MinimumConstraint x => new("minimum", x.Value),
        Native.MaximumConstraint x => new("maximum", x.Value),
        Native.ExclusiveMinimumConstraint x => new("exclusiveMinimum", x.Value),
        Native.ExclusiveMaximumConstraint x => new("exclusiveMaximum", x.Value),
        Native.PatternConstraint x => new("pattern", x.Value),
        Native.EnumConstraint x => new("enum", x.Value),
        Native.JsonSchemaConstraint x => new("jsonSchema", x.Value),
        Native.UnknownConstraint x => new(x.Name, x.Value),
        _ => new(constraint.GetType().Name, constraint.Value)
    };

    private static AssetKind MapAssetKind(Native.Resource resource) =>
        (resource.Type ?? resource.Kind)?.ToLowerInvariant() switch
        {
            "table" => AssetKind.Table, "view" => AssetKind.View, "file" => AssetKind.File,
            "object" => AssetKind.Object, _ when resource.Connection is not null => AssetKind.Table,
            _ when resource.Paths.Count > 0 => AssetKind.File, _ => AssetKind.Unknown
        };

    private static Ownership? MapOwnership(IEnumerable<Native.Contributor> contributors)
    {
        var values = contributors.ToArray();
        if (values.Length == 0) return null;
        return new Ownership(string.Join(", ", values.Select(x => x.Title ??
            string.Join(" ", new[] { x.GivenName, x.FamilyName }.Where(v => !string.IsNullOrWhiteSpace(v))))),
            values.SelectMany(x => x.Roles ?? []).Distinct().ToArray());
    }

    private static IReadOnlyList<AuthoritativeReference> MapReferences(Native.DataPackage package)
    {
        var values = new List<AuthoritativeReference>();
        if (!string.IsNullOrWhiteSpace(package.Homepage)) values.Add(new(package.Homepage, "homepage"));
        values.AddRange(package.Sources.Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .Select(x => new AuthoritativeReference(x.Path!, "source", x.Title)));
        values.AddRange(package.Resources.Where(x => !string.IsNullOrWhiteSpace(x.Homepage))
            .Select(x => new AuthoritativeReference(x.Homepage!, "resource-homepage", x.Title)));
        values.AddRange(package.Resources.SelectMany(x => x.Sources).Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .Select(x => new AuthoritativeReference(x.Path!, "source", x.Title)));
        return values;
    }

    private static IReadOnlyDictionary<string, object?> ResourceExtensions(Native.Resource resource) =>
        new Dictionary<string, object?> { ["profile"] = resource.Profile, ["type"] = resource.Type,
            ["kind"] = resource.Kind, ["homepage"] = resource.Homepage,
            ["sources"] = resource.Sources, ["licenses"] = resource.Licenses };

    private static IReadOnlyDictionary<string, object?> FieldExtensions(Native.Field field)
    {
        var values = new Dictionary<string, object?>
        {
            ["title"] = field.Title, ["description"] = field.Description,
            ["example"] = field.Example, ["rdfType"] = field.RdfType,
            ["categories"] = field.Categories, ["categoriesOrdered"] = field.CategoriesOrdered
        };
        if (field is Native.BooleanField boolean)
        {
            values["trueValues"] = boolean.TrueValues;
            values["falseValues"] = boolean.FalseValues;
        }
        return values;
    }

    private static object? NormalizeInlineValue(object? value) => value switch
    {
        null => null,
        JValue scalar => scalar.Value,
        JObject map => map.Properties().ToDictionary(
            property => property.Name, property => NormalizeInlineValue(property.Value)),
        JArray sequence => sequence.Select(NormalizeInlineValue).ToArray(),
        IDictionary map => map.Cast<DictionaryEntry>().ToDictionary(
            entry => entry.Key?.ToString() ?? string.Empty, entry => NormalizeInlineValue(entry.Value)),
        IEnumerable sequence when value is not string => sequence.Cast<object?>()
            .Select(NormalizeInlineValue).ToArray(),
        _ => value
    };

    private static IReadOnlyDictionary<string, object?> DialectOptions(Native.TableDialect? dialect)
    {
        var values = new Dictionary<string, object?>();
        if (dialect is null) return values;
        values["dialectType"] = dialect.Type;
        values["dialectProfile"] = dialect.Profile;
        values["header"] = dialect.Header; values["headerRows"] = dialect.HeaderRows;
        values["headerJoin"] = dialect.HeaderJoin; values["commentRows"] = dialect.CommentRows;
        values["commentChar"] = dialect.CommentChar; values["delimiter"] = dialect.Delimiter;
        values["lineTerminator"] = dialect.LineTerminator; values["quoteChar"] = dialect.QuoteChar;
        values["doubleQuote"] = dialect.DoubleQuote; values["escapeChar"] = dialect.EscapeChar;
        values["nullSequence"] = dialect.NullSequence; values["skipInitialSpace"] = dialect.SkipInitialSpace;
        values["property"] = dialect.Property; values["itemType"] = dialect.ItemType;
        values["itemKeys"] = dialect.ItemKeys; values["sheetName"] = dialect.SheetName;
        values["sheetNumber"] = dialect.SheetNumber; values["table"] = dialect.Table;
        if (dialect is Native.TableDelimitedDialect delimited) values["headerRepeat"] = delimited.HeaderRepeat;
        if (dialect is Native.TableDatabaseDialect database) values["namespace"] = database.Namespace;
        return values;
    }
}
