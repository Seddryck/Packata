using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Packata.ConsumerContracts;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using Spectre.Console.Cli;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

internal sealed class SatisfiesSettings : CommandSettings
{
    [CommandArgument(0, "[CONTRACT]")]
    [Description("Producer ODCS or Data Package YAML/JSON file.")]
    public string? Contract { get; init; }

    [CommandOption("-r|--requirements <FILE>")]
    [Description("Consumer Contract Requirements YAML/JSON file.")]
    public string? Requirements { get; init; }

    [CommandOption("--format <FORMAT>")]
    [DefaultValue("text")]
    [Description("Output format: text or json.")]
    public string Format { get; init; } = "text";

    [CommandOption("--quiet")]
    [Description("Suppress all output and return only an exit code.")]
    public bool Quiet { get; init; }
}

internal sealed class SatisfiesCommand : Command<SatisfiesSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public override int Execute(CommandContext context, SatisfiesSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Format is not ("text" or "json")) throw new ArgumentException("--format must be text or json.");
            ArgumentException.ThrowIfNullOrWhiteSpace(settings.Contract);
            if (string.IsNullOrWhiteSpace(settings.Requirements)) throw new ArgumentException("--requirements is required.");
            var requirementsText = File.ReadAllText(settings.Requirements);
            var consumer = ConsumerContractSerializer.Deserialize(requirementsText,
                Path.GetExtension(settings.Requirements).Equals(".json", StringComparison.OrdinalIgnoreCase));
            var producer = LoadProducer(settings.Contract);
            var result = producer.Satisfies(consumer);
            if (!settings.Quiet)
            {
                if (settings.Format == "json") Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else WriteText(result);
            }
            return result.IsSatisfied ? 0 : 1;
        }
        catch (Exception exception)
        {
            if (!settings.Quiet)
            {
                if (settings.Format == "json") Console.WriteLine(JsonSerializer.Serialize(new { error = exception.Message }, JsonOptions));
                else Console.Error.WriteLine("Error: " + exception.Message);
            }
            return 2;
        }
    }

    private static DataContract LoadProducer(string path)
    {
        var text = File.ReadAllText(path);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new ArgumentException("Expected a single ODCS or Data Package document.");
        var contract = DeserializeProducer(path, root);
        ValidateProducer(contract);
        return contract;
    }

    private static DataContract DeserializeProducer(string path, YamlMappingNode root)
    {
        if (root.Children.TryGetValue(new YamlScalarNode("kind"), out var kind))
        {
            if (kind is not YamlScalarNode { Value: "DataContract" }
                || !root.Children.ContainsKey(new YamlScalarNode("apiVersion"))
                || !root.Children.ContainsKey(new YamlScalarNode("schema")))
                throw new ArgumentException("Invalid ODCS document: expected DataContract, apiVersion and schema.");
            return new Packata.OpenDataContract.DataContractFactory().LoadFromFile(path)
                .ToCanonicalContract().RequireValue();
        }
        if (!root.Children.ContainsKey(new YamlScalarNode("resources")))
            throw new ArgumentException("Invalid Data Package document: resources are required.");
        return ContractDocument.Load(path).Contract;
    }

    private static void ValidateProducer(DataContract contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract.Identity.Id);
        if (HasInvalidNames(contract.Assets.Select(x => x.Name)))
            throw new ArgumentException("Producer dataset and field names must be non-empty and unique.");
        foreach (var asset in contract.Assets)
            ValidateProducerFields(asset.Schema?.Fields ?? []);
    }

    private static bool HasInvalidNames(IEnumerable<string> names)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        return names.Any(name => string.IsNullOrWhiteSpace(name) || !unique.Add(name));
    }

    private static void ValidateProducerFields(IReadOnlyList<DataField> fields)
    {
        if (HasInvalidNames(fields.Select(x => x.Name)))
            throw new ArgumentException("Producer dataset and field names must be non-empty and unique.");
        if (fields.Any(field => field.Length < 0 || field.Precision <= 0 || field.Scale < 0 || field.Scale > field.Precision))
            throw new ArgumentException("Producer length, precision, or scale is invalid.");
    }
    private static void WriteText(ContractSatisfaction result)
    {
        Console.WriteLine(result.IsSatisfied ? "✓ Consumer requirements satisfied" : "✗ Consumer requirements not satisfied");
        foreach (var group in result.Results.GroupBy(x => x.Dataset))
        {
            Console.WriteLine();
            if (group.Key is not null) Console.WriteLine(group.Key);
            foreach (var item in group)
            {
                var symbol = item.Status switch { SatisfactionStatus.Satisfied => "✓", SatisfactionStatus.Unsatisfied => "✗", _ => "?" };
                Console.WriteLine($"  {symbol} {item.Field ?? item.Constraint}: {item.Message}");
            }
        }
    }
}
