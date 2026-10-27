namespace Packata.Core.Contracts;

/// <summary>A semantic path. Dots and backslashes in identifiers are escaped with a backslash.</summary>
public sealed record ContractPath
{
    public string Value { get; }

    public ContractPath(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public ContractPath Append(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        return new($"{Value}.{Escape(identifier)}");
    }

    public static ContractPath Root(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        return new(Escape(identifier));
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace(".", "\\.");
    public override string ToString() => Value;
}

public interface IContractChange { ContractPath Path { get; } }
public interface IContractChangeOperation;
public interface IAdded : IContractChangeOperation;
public interface IRemoved : IContractChangeOperation;
public interface IModified : IContractChangeOperation;
public interface IContractChangeAsset;
public interface IServerChange : IContractChangeAsset;
public interface ISlaChange : IContractChangeAsset;
public interface IDatasetChange : IContractChangeAsset;
public interface IPropertyChange : IContractChangeAsset
{
    string DatasetId { get; }
    string PropertyId { get; }
}
public interface IRelationshipChange : IContractChangeAsset;
public interface IQualityRuleChange : IContractChangeAsset;
public interface IContractMetadataChange : IContractChangeAsset;

public sealed record ServerAdded(ContractPath Path, DataEndpoint Server) : IContractChange, IAdded, IServerChange;
public sealed record ServerRemoved(ContractPath Path, DataEndpoint Server) : IContractChange, IRemoved, IServerChange;
public sealed record ServerModified(ContractPath Path, DataEndpoint Previous, DataEndpoint Current) : IContractChange, IModified, IServerChange;
public sealed record SlaAdded(ContractPath Path, ServiceLevel Sla) : IContractChange, IAdded, ISlaChange;
public sealed record SlaRemoved(ContractPath Path, ServiceLevel Sla) : IContractChange, IRemoved, ISlaChange;
public sealed record SlaModified(ContractPath Path, ServiceLevel Previous, ServiceLevel Current) : IContractChange, IModified, ISlaChange;
public sealed record DatasetAdded(ContractPath Path, DataAsset Dataset) : IContractChange, IAdded, IDatasetChange;
public sealed record DatasetRemoved(ContractPath Path, DataAsset Dataset) : IContractChange, IRemoved, IDatasetChange;
public sealed record DatasetModified(ContractPath Path, DataAsset Previous, DataAsset Current) : IContractChange, IModified, IDatasetChange;
public sealed record PropertyAdded(ContractPath Path, string DatasetId, string PropertyId, DataField Property) : IContractChange, IAdded, IPropertyChange;
public sealed record PropertyRemoved(ContractPath Path, string DatasetId, string PropertyId, DataField Property) : IContractChange, IRemoved, IPropertyChange;
public sealed record PropertyModified(ContractPath Path, string DatasetId, string PropertyId, DataField Previous, DataField Current) : IContractChange, IModified, IPropertyChange;
public sealed record PropertyTypeChanged(ContractPath Path, string DatasetId, string PropertyId, string? Previous, string? Current) : IContractChange, IModified, IPropertyChange;
public sealed record RelationshipAdded(ContractPath Path, DataRelationship Relationship) : IContractChange, IAdded, IRelationshipChange;
public sealed record RelationshipRemoved(ContractPath Path, DataRelationship Relationship) : IContractChange, IRemoved, IRelationshipChange;
public sealed record RelationshipModified(ContractPath Path, DataRelationship Previous, DataRelationship Current) : IContractChange, IModified, IRelationshipChange;
public sealed record QualityRuleAdded(ContractPath Path, DataQualityRule QualityRule) : IContractChange, IAdded, IQualityRuleChange;
public sealed record QualityRuleRemoved(ContractPath Path, DataQualityRule QualityRule) : IContractChange, IRemoved, IQualityRuleChange;
public sealed record QualityRuleModified(ContractPath Path, DataQualityRule Previous, DataQualityRule Current) : IContractChange, IModified, IQualityRuleChange
{
    public bool MeasurementChanged => !ContractDiffValues.Equal(Previous.Definition, Current.Definition);
    public bool ExpectationsChanged => !ContractDiffValues.Equal(Previous.Expectations, Current.Expectations);
    public bool MetadataChanged => !ContractDiffValues.Equal(Previous.Metadata, Current.Metadata)
        || Previous.Description != Current.Description || !ContractDiffValues.Equal(Previous.Extensions, Current.Extensions);
}

/// <summary>A modification to contract identity, metadata, governance, or extension metadata.</summary>
public sealed record ContractMetadataChanged(ContractPath Path, object? Previous, object? Current) : IContractChange, IModified, IContractMetadataChange;

public sealed class ContractDiff
{
    public IReadOnlyList<IContractChange> Changes { get; }

    internal ContractDiff(IEnumerable<IContractChange> changes)
        => Changes = Array.AsReadOnly(changes.OrderBy(x => x.Path.Value, StringComparer.Ordinal)
            .ThenBy(x => x.GetType().Name, StringComparer.Ordinal).ToArray());
}
