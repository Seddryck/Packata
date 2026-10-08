namespace Packata.Core.Contracts;

/// <summary>
/// Standards-neutral representation of a contract describing one or more data assets.
/// </summary>
public sealed record DataContract(
    ContractIdentity Identity,
    ContractMetadata Metadata,
    IReadOnlyList<DataAsset> Assets,
    IReadOnlyList<DataEndpoint> Endpoints,
    ContractGovernance Governance,
    ExtensionMetadata? Extensions = null)
{
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public sealed record ContractIdentity(
    string Id,
    string? Name = null,
    string? Version = null,
    string? Status = null);

public sealed record ContractMetadata(
    string? Title = null,
    string? Description = null,
    string? Domain = null,
    IReadOnlyList<string>? Tags = null)
{
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];
}

public sealed record DataAsset(
    string Id,
    string Name,
    string? PhysicalName,
    string? Description,
    AssetKind Kind,
    DataSchema? Schema,
    IReadOnlyList<EndpointBinding>? EndpointBindings = null,
    IReadOnlyList<DataQualityRule>? QualityRules = null,
    ExtensionMetadata? Extensions = null)
{
    public IReadOnlyList<EndpointBinding> EndpointBindings { get; init; } = EndpointBindings ?? [];
    public IReadOnlyList<DataQualityRule> QualityRules { get; init; } = QualityRules ?? [];
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public enum AssetKind
{
    Unknown,
    Table,
    View,
    Topic,
    File,
    Object
}

public sealed record DataSchema(
    IReadOnlyList<DataField> Fields,
    IReadOnlyList<string>? PrimaryKey = null,
    IReadOnlyList<DataRelationship>? Relationships = null,
    ExtensionMetadata? Extensions = null)
{
    public IReadOnlyList<string> PrimaryKey { get; init; } = PrimaryKey ?? [];
    public IReadOnlyList<DataRelationship> Relationships { get; init; } = Relationships ?? [];
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public sealed record DataField(
    string Name,
    string? LogicalType,
    string? PhysicalType = null,
    string? Format = null,
    bool Required = false,
    IReadOnlyList<DataConstraint>? Constraints = null,
    IReadOnlyList<DataQualityRule>? QualityRules = null,
    IReadOnlyList<DataField>? Children = null,
    ExtensionMetadata? Extensions = null,
    string? PhysicalName = null,
    string? Description = null)
{
    public IReadOnlyList<DataConstraint> Constraints { get; init; } = Constraints ?? [];
    public IReadOnlyList<DataQualityRule> QualityRules { get; init; } = QualityRules ?? [];
    public IReadOnlyList<DataField> Children { get; init; } = Children ?? [];
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public sealed record DataConstraint(string Kind, object? Value);

public sealed record DataRelationship(
    IReadOnlyList<string> Fields,
    string TargetAsset,
    IReadOnlyList<string> TargetFields,
    string? Name = null,
    string Kind = "foreignKey",
    ExtensionMetadata? Extensions = null)
{
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public sealed record DataQualityRule(
    string Kind,
    string? Description = null,
    string? Expression = null);

public sealed record EndpointBinding(string EndpointId, string? AssetPath = null);

public sealed record DataEndpoint(
    string Id,
    string? Name,
    EndpointKind Kind,
    string? Environment,
    DataLocation Location,
    DataFormat? Format = null,
    ExtensionMetadata? Extensions = null)
{
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public enum EndpointKind
{
    Unknown,
    File,
    Database,
    ObjectStorage,
    Stream,
    Api,
    Inline
}

public abstract record DataLocation;

public sealed record PathLocation(IReadOnlyList<string> Paths) : DataLocation;

public sealed record ConnectionLocation(
    string Scheme,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? Namespace = null,
    string? ConnectionUrl = null,
    string? Catalog = null) : DataLocation;

public sealed record InlineLocation(object? Value) : DataLocation;

public sealed record DataFormat(
    string? Name,
    string? MediaType = null,
    string? Encoding = null,
    string? Compression = null,
    IReadOnlyDictionary<string, object?>? Options = null)
{
    public IReadOnlyDictionary<string, object?> Options { get; init; } =
        Options ?? new Dictionary<string, object?>();
}

public sealed record ContractGovernance(
    Ownership? Ownership = null,
    TermsOfUse? Terms = null,
    IReadOnlyList<ServiceLevel>? ServiceLevels = null,
    IReadOnlyList<AuthoritativeReference>? References = null,
    ExtensionMetadata? Extensions = null)
{
    public IReadOnlyList<ServiceLevel> ServiceLevels { get; init; } = ServiceLevels ?? [];
    public IReadOnlyList<AuthoritativeReference> References { get; init; } = References ?? [];
    public ExtensionMetadata Extensions { get; init; } = Extensions ?? ExtensionMetadata.Empty;
}

public sealed record Ownership(
    string? Owner,
    IReadOnlyList<string>? Roles = null)
{
    public IReadOnlyList<string> Roles { get; init; } = Roles ?? [];
}

public sealed record TermsOfUse(
    string? Usage = null,
    string? Limitations = null,
    string? Billing = null);

public sealed record ServiceLevel(
    string Name,
    string? Description = null,
    object? Value = null);

public sealed record AuthoritativeReference(
    string Url,
    string? Type = null,
    string? Description = null);
