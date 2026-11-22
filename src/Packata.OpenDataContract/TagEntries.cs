using System.Collections;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract;

/// <summary>A proposed ODCS key/value tag. The value retains its scalar type.</summary>
public sealed record KeyValueTag
{
    public KeyValueTag(string key, object value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Key = key;
        Value = ScalarValue.From(value);
        if (Value.Kind == ScalarKind.Null)
            throw new ArgumentException("A tag value must be a string, number, or boolean.", nameof(value));
    }

    public string Key { get; }
    public ScalarValue Value { get; }
}

/// <summary>An ordered sequence of plain string labels and <see cref="KeyValueTag"/> entries.</summary>
public sealed class TagEntries : IReadOnlyList<object>
{
    private readonly object[] entries;

    public TagEntries(IEnumerable<object> entries)
    {
        this.entries = entries.ToArray();
        if (this.entries.Any(entry => entry is not (string or KeyValueTag)))
            throw new ArgumentException("A tag entry must be a string or key/value tag.", nameof(entries));
    }

    public int Count => entries.Length;
    public object this[int index] => entries[index];
    public IEnumerator<object> GetEnumerator() => ((IEnumerable<object>)entries).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal IEnumerable<string> Labels => entries.OfType<string>();
    internal bool HasKeyValueTags => entries.Any(entry => entry is KeyValueTag);

    internal TagEntries WithLabels(IReadOnlyList<string> labels)
    {
        if (entries.Length == 0 && labels.Count == 0) return this;
        if (!HasKeyValueTags) return new(labels);

        // The existing mutable Tags property owns the plain labels. Keep the positions
        // of keyed tags when callers read, replace, or edit that list.
        var merged = new List<object>();
        var index = 0;
        foreach (var entry in entries)
        {
            if (entry is KeyValueTag) merged.Add(entry);
            else if (index < labels.Count) merged.Add(labels[index++]);
        }
        for (; index < labels.Count; index++) merged.Add(labels[index]);
        return new(merged);
    }

    internal IReadOnlyList<object?> Snapshot() => Array.AsReadOnly(entries.Select(entry =>
        entry is KeyValueTag tag ? MetadataSnapshot.CopyDictionary(
            new Dictionary<string, object?> { [tag.Key] = tag.Value.Value }) : entry).ToArray());
}
