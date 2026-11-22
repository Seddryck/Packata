using Packata.Core.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

internal sealed class TagEntriesConverter(YamlMappingNode? root = null) : IYamlTypeConverter
{
    private readonly Lazy<Dictionary<long, YamlSequenceNode>> sequences = new(() => FindSequences(root));

    public bool Accepts(Type type) => type == typeof(TagEntries);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (!parser.Accept<SequenceStart>(out var start))
            throw new YamlException("Expected a sequence of tags.");

        // Read resolved nodes so aliases keep the original scalar styles and types.
        var entries = new TagEntries(sequences.Value[start.Start.Index].Children.Select(ReadEntry));
        // Let the collection deserializer consume the events and register anchors for later properties.
        rootDeserializer(typeof(List<object>));
        return entries;
    }

    private static object ReadEntry(YamlNode node)
    {
        if (node is YamlScalarNode scalar && ReadScalar(scalar) is string label) return label;
        if (node is YamlMappingNode mapping && mapping.Children.Count == 1)
        {
            var entry = mapping.Children.Single();
            if (entry.Key is YamlScalarNode { Value: { Length: > 0 } key } &&
                entry.Value is YamlScalarNode item && ReadScalar(item) is { } value)
                return new KeyValueTag(key, value);
        }
        throw new YamlException(node.Start, node.End, "Expected a string tag or exactly one non-empty key with a string, number, or boolean value.");
    }

    private static Dictionary<long, YamlSequenceNode> FindSequences(YamlMappingNode? root)
    {
        var sequences = new Dictionary<long, YamlSequenceNode>();
        var visited = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<YamlNode>();
        if (root is not null) pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (node is YamlMappingNode mapping)
                foreach (var value in mapping.Children.Values) pending.Push(value);
            if (node is YamlSequenceNode sequence)
            {
                sequences.Add(sequence.Start.Index, sequence);
                foreach (var child in sequence.Children) pending.Push(child);
            }
        }
        return sequences;
    }

    private static object? ReadScalar(YamlScalarNode node)
    {
        var scalar = new Scalar(node.Anchor, node.Tag, node.Value ?? "", node.Style, true, false, node.Start, node.End);
        return IsRadixInteger(scalar) ? ReadRadixInteger(scalar) : ReadTypedScalar(scalar);
    }

    private static long ReadRadixInteger(Scalar scalar)
    {
        try
        {
            var radix = scalar.Value[1] == 'x' ? 16 : 8;
            var number = Convert.ToUInt64(scalar.Value[2..], radix);
            if (number <= long.MaxValue) return (long)number;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new YamlException(scalar.Start, scalar.End, "Invalid or out-of-range tag integer.");
        }
        throw new YamlException(scalar.Start, scalar.End, "Tag integer is outside the signed 64-bit range.");
    }

    private static object? ReadTypedScalar(Scalar scalar)
    {
        var value = EnumerationValueConverter.ReadScalar(scalar);
        if (scalar.Tag.IsEmpty || scalar.Tag.IsNonSpecific) return value;
        var valid = scalar.Tag.Value switch
        {
            "tag:yaml.org,2002:str" => value is string,
            "tag:yaml.org,2002:bool" => value is bool,
            "tag:yaml.org,2002:int" => value is long,
            "tag:yaml.org,2002:float" => value is decimal or double,
            "tag:yaml.org,2002:null" => value is null,
            _ => false
        };
        return valid ? value : throw new YamlException(scalar.Start, scalar.End, "Invalid explicit YAML type on an ODCS tag value.");
    }

    private static bool IsRadixInteger(Scalar scalar)
        => (scalar.Tag == "tag:yaml.org,2002:int" ||
            ((scalar.Tag.IsEmpty || scalar.Tag.IsNonSpecific) && scalar.Style == ScalarStyle.Plain)) &&
           (scalar.Value.StartsWith("0x", StringComparison.Ordinal) || scalar.Value.StartsWith("0o", StringComparison.Ordinal)) &&
           scalar.Value.Length > 2 && scalar.Value[2..].All(char.IsAsciiHexDigit);

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, SequenceStyle.Block));
        foreach (var entry in (TagEntries)value!)
        {
            if (entry is string label)
            {
                EnumerationValueConverter.WriteScalar(emitter, ScalarValue.From(label), serializer);
                continue;
            }

            var tag = (KeyValueTag)entry;
            emitter.Emit(new MappingStart());
            EnumerationValueConverter.WriteScalar(emitter, ScalarValue.From(tag.Key), serializer);
            EnumerationValueConverter.WriteScalar(emitter, tag.Value, serializer);
            emitter.Emit(new MappingEnd());
        }
        emitter.Emit(new SequenceEnd());
    }
}
