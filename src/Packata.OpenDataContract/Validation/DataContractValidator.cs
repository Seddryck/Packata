using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace Packata.OpenDataContract.Validation;

internal static partial class DataContractValidator
{
    public static void Validate(DataContract contract)
    {
        foreach (var schema in contract.Schema)
        {
            ValidateRelationships(schema.Relationships, $"schema/{schema.Name}/relationships");
            foreach (var property in schema.Properties)
                ValidateRelationships(property.Relationships, $"schema/{schema.Name}/properties/{property.Name}/relationships");
        }
    }

    private static void ValidateRelationships(IEnumerable<Relationship> relationships, string path)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in relationships)
        {
            if (relationship.Id is null)
                continue;

            if (InvalidStableIdCharacter().IsMatch(relationship.Id))
                throw new YamlException($"Relationship id '{relationship.Id}' at {path} contains a prohibited character.");

            if (!identifiers.Add(relationship.Id))
                throw new YamlException($"Relationship id '{relationship.Id}' is duplicated at {path}.");
        }
    }

    [GeneratedRegex(@"[.#/\\@!%&^\s]")]
    private static partial Regex InvalidStableIdCharacter();
}
