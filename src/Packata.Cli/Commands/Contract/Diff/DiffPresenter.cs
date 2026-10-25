using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Packata.Core.Contracts;

namespace Packata.Cli;

internal static class DiffPresenter
{
    internal static JObject Result(string baseline, string candidate, ContractDiff diff, IReadOnlyList<ComparisonDiagnostic> diagnostics)
        => (JObject)CommandOutput.RedactedJson(new
        {
            schemaVersion = 1, baseline, candidate, hasChanges = diff.Changes.Count != 0,
            changes = diff.Changes.Select(Change).ToArray(), diagnostics
        });

    internal static JObject Change(IContractChange change)
    {
        var operation = change switch
        {
            IAdded => "added", IRemoved => "removed", IModified => "modified",
            _ => throw Unsupported()
        };
        var asset = change switch
        {
            IServerChange => "server", ISlaChange => "sla", IDatasetChange => "dataset",
            IPropertyChange => "property", IRelationshipChange => "relationship",
            IQualityRuleChange => "qualityRule", IContractMetadataChange => "contractMetadata",
            _ => throw Unsupported()
        };
        // Exhaustive typed projection: new library kinds must be explicitly supported,
        // rather than being serialized as an empty IContractChange or silently omitted.
        var (kind, payload) = change switch
        {
            ServerAdded x => (nameof(ServerAdded), (object)new { x.Server }),
            ServerRemoved x => (nameof(ServerRemoved), new { x.Server }),
            ServerModified x => (nameof(ServerModified), new { x.Previous, x.Current }),
            SlaAdded x => (nameof(SlaAdded), new { x.Sla }),
            SlaRemoved x => (nameof(SlaRemoved), new { x.Sla }),
            SlaModified x => (nameof(SlaModified), new { x.Previous, x.Current }),
            DatasetAdded x => (nameof(DatasetAdded), new { x.Dataset }),
            DatasetRemoved x => (nameof(DatasetRemoved), new { x.Dataset }),
            DatasetModified x => (nameof(DatasetModified), new { x.Previous, x.Current }),
            PropertyAdded x => (nameof(PropertyAdded), new { x.DatasetId, x.PropertyId, x.Property }),
            PropertyRemoved x => (nameof(PropertyRemoved), new { x.DatasetId, x.PropertyId, x.Property }),
            PropertyModified x => (nameof(PropertyModified), new { x.DatasetId, x.PropertyId, x.Previous, x.Current }),
            PropertyTypeChanged x => (nameof(PropertyTypeChanged), new { x.DatasetId, x.PropertyId, x.Previous, x.Current }),
            RelationshipAdded x => (nameof(RelationshipAdded), new { x.Relationship }),
            RelationshipRemoved x => (nameof(RelationshipRemoved), new { x.Relationship }),
            RelationshipModified x => (nameof(RelationshipModified), new { x.Previous, x.Current }),
            QualityRuleAdded x => (nameof(QualityRuleAdded), new { x.QualityRule }),
            QualityRuleRemoved x => (nameof(QualityRuleRemoved), new { x.QualityRule }),
            QualityRuleModified x => (nameof(QualityRuleModified), new { x.Previous, x.Current }),
            ContractMetadataChanged x => (nameof(ContractMetadataChanged), new { x.Previous, x.Current }),
            _ => throw Unsupported()
        };
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            },
            Converters = { new StringEnumConverter(), new ExtensionConverter() }
        });
        var result = new JObject { ["kind"] = kind, ["operation"] = operation, ["asset"] = asset, ["path"] = change.Path.Value };
        result.Merge(JObject.FromObject(payload, serializer));
        return (JObject)CommandOutput.RedactedJson(result);
    }

    internal static void Text(JObject result, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine($"Baseline (previous): {result["baseline"]}");
        writer.WriteLine($"Candidate (current): {result["candidate"]}");
        writer.WriteLine("Direction: baseline -> candidate");
        if (!(bool)result["hasChanges"]!) writer.WriteLine("No differences detected.");
        foreach (var change in result["changes"]!.Cast<JObject>())
        {
            writer.WriteLine($"{change["operation"]} {change["asset"]} {change["path"]} ({change["kind"]})");
            foreach (var property in change.Properties().Skip(4))
                writer.WriteLine($"  {property.Name}: {property.Value.ToString(Formatting.None)}");
        }
    }

    private static ContractOperationException Unsupported()
        => new("CLI020", "$", "The library returned an unsupported contract change kind or classification.");

    private sealed class ExtensionConverter : JsonConverter<ExtensionMetadata>
    {
        public override bool CanRead => false;
        public override ExtensionMetadata? ReadJson(JsonReader reader, Type objectType, ExtensionMetadata? existingValue, bool hasExistingValue, JsonSerializer serializer)
            => throw new NotSupportedException();
        public override void WriteJson(JsonWriter writer, ExtensionMetadata? value, JsonSerializer serializer)
            => serializer.Serialize(writer, value?.Namespaces.Order(StringComparer.Ordinal).ToDictionary(key => key, key => value[key]));
    }
}
