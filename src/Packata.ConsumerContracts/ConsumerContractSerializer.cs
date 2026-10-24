using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.ConsumerContracts;

/// <summary>Versioned CCR YAML and JSON serialization. Both forms share one semantic model.</summary>
public static class ConsumerContractSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    public static ConsumerContract Deserialize(string content, bool json = false)
    {
        var document = json ? JsonSerializer.Deserialize<Document>(content, JsonOptions)
            : new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithDuplicateKeyChecking().Build().Deserialize<Document>(content);
        if (document is null || document.ApiVersion != "v1" || document.Kind != "ConsumerContract")
            throw new ArgumentException("Expected apiVersion v1 and kind ConsumerContract.");
        if (document.Datasets is null || document.Datasets.Any(d => d is null || d.Fields is null || d.Fields.Any(f => f is null)))
            throw new ArgumentException("Dataset and field lists cannot be null.");
        var contract = new ConsumerContract(document.Id, document.Version,
            document.Target is null ? null : new ContractTarget(document.Target.Id),
            document.Datasets.Select(d => (IDatasetRequirement)new DatasetRequirement(d.Name,
                d.Fields.Select(f => (IFieldRequirement)new FieldRequirement(f.Name,
                    f.Type is null ? null : new LogicalType(f.Type), f.Nullable, f.Length, f.Precision, f.Scale)).ToArray())).ToArray());
        contract.Validate();
        return contract;
    }
    public static string Serialize(IConsumerContract contract, bool json = false)
    {
        new ConsumerContract(contract.Id, contract.Version, contract.Target, contract.Datasets).Validate();
        var document = new Document
        {
            ApiVersion = "v1", Kind = "ConsumerContract",
            Id = contract.Id, Version = contract.Version,
            Target = contract.Target is null ? null : new TargetDocument { Id = contract.Target.Id },
            Datasets = contract.Datasets.Select(d => new DatasetDocument
            {
                Name = d.Name, Fields = d.Fields.Select(f => new FieldDocument { Name = f.Name, Type = f.Type?.Name, Nullable = f.Nullable, Length = f.Length, Precision = f.Precision, Scale = f.Scale }).ToList()
            }).ToList()
        };
        return json ? JsonSerializer.Serialize(document, JsonOptions)
            : new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build().Serialize(document);
    }
    public sealed class Document
    {
        public string? ApiVersion { get; set; }
        public string? Kind { get; set; }
        public string Id { get; set; } = "";
        public string? Version { get; set; }
        public TargetDocument? Target { get; set; }
        public List<DatasetDocument> Datasets { get; set; } = [];
    }
    public sealed class TargetDocument { public string Id { get; set; } = ""; }
    public sealed class DatasetDocument
    {
        public string Name { get; set; } = "";
        public List<FieldDocument> Fields { get; set; } = [];
    }
    public sealed class FieldDocument
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public bool? Nullable { get; set; }
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
    }
}
