using YamlDotNet.RepresentationModel;

namespace Packata.Application;

/// <summary>Bounded descriptor syntax reusable for recognition, inspection and source projection.</summary>
public sealed class ContractSyntax
{
    private ContractSyntax(string text, YamlMappingNode root) => (Text, Root) = (text, root);

    public string Text { get; }
    public YamlMappingNode Root { get; }
    public bool Recognized => ContractDocument.Recognizes(Root);

    public static ContractSyntax Parse(string text) => new(text, ContractDocument.ParseRoot(text));
}
