namespace Packata.OpenDataContract;

using YamlDotNet.Serialization;

public class Synonym
{
    public string? Id { get; set; }
    [YamlMember(Alias = "synonym")]
    public required string SynonymValue { get; set; }
    public string? Description { get; set; }
    public string? Locale { get; set; }
    public string? Source { get; set; }
    public string? Status { get; set; }
    public CustomProperties CustomProperties { get; set; } = [];
}
