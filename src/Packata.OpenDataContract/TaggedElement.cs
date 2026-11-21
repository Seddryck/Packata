using Packata.Core;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract;

/// <summary>Shares ordered tag entries and mutable plain labels across ODCS elements.</summary>
/// <typeparam name="TLabels">The element's existing array or list type for plain labels.</typeparam>
public abstract class TaggedElement<TLabels>(Func<IEnumerable<string>, TLabels> createLabels)
    where TLabels : IReadOnlyList<string>
{
    private TagEntries tagEntries = new([]);

    /// <summary>Gets or sets the plain string labels assigned to the element.</summary>
    /// <value>The mutable labels, using the element's existing collection type.</value>
    [Label("Tags")]
    [YamlIgnore]
    public TLabels Tags { get; set; } = createLabels([]);

    /// <summary>Gets or sets the ordered string and key/value tags.</summary>
    /// <value>Plain labels and key/value entries in declaration order.</value>
    [YamlMember(Alias = "tags", ApplyNamingConventions = false)]
    [System.Text.Json.Serialization.JsonIgnore]
    public TagEntries TagEntries
    {
        get => tagEntries.WithLabels(Tags);
        set
        {
            tagEntries = value;
            Tags = createLabels(value.Labels);
        }
    }
}
