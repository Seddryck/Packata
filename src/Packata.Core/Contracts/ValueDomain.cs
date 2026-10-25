namespace Packata.Core.Contracts;

public sealed record ValueOption
{
    public ScalarValue Value { get; }
    public string? Label { get; }
    public string? Description { get; }
    public ExtensionMetadata Extensions { get; }
    public ValueOption(ScalarValue value, string? label = null, string? description = null, ExtensionMetadata? extensions = null)
        => (Value, Label, Description, Extensions) = (value, label, description, MetadataSnapshot.CopyExtensions(extensions));
}

/// <summary>A descriptive catalog or a reference, independently of the effective membership restriction.</summary>
public sealed class ValueDomain
{
    public IReadOnlyList<ValueOption>? Entries { get; }
    /// <summary>Null means finite membership is unknown, not unrestricted. Empty means no values are allowed.</summary>
    public IReadOnlyList<ScalarValue>? AllowedValues { get; }
    public bool? Ordered { get; }
    public ValueDomainReference? Reference { get; }
    public ExtensionMetadata Extensions { get; }

    public ValueDomain(IEnumerable<ValueOption>? entries = null, IEnumerable<ScalarValue>? allowedValues = null,
        bool? ordered = null, ValueDomainReference? reference = null, ExtensionMetadata? extensions = null)
    {
        if ((entries is null) == (reference is null)) throw new ArgumentException("Supply either inline entries or a reference.");
        Entries = entries is null ? null : Array.AsReadOnly(entries.ToArray());
        AllowedValues = allowedValues is null ? null : Array.AsReadOnly(allowedValues.ToArray());
        if (Entries is not null && (AllowedValues is null || Entries.Any(x => x is null) || Entries.Select(x => x.Value).Distinct().Count() != Entries.Count))
            throw new ArgumentException("Inline entries require known membership and unique, non-null options.");
        if (AllowedValues is not null && AllowedValues.Distinct().Count() != AllowedValues.Count)
            throw new ArgumentException("Allowed values must be unique.");
        if (Entries is not null && AllowedValues!.Any(value => !Entries.Any(entry => entry.Value == value)))
            throw new ArgumentException("Allowed values must be a subset of the inline catalog with matching scalar kinds.");
        (Ordered, Reference, Extensions) = (ordered, reference, MetadataSnapshot.CopyExtensions(extensions));
    }

    internal IReadOnlyList<DataConstraint> ProjectConstraints(IReadOnlyList<DataConstraint> constraints)
    {
        var declarations = constraints.Where(value => value.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (AllowedValues is null)
        {
            if (declarations.Any(declaration => declaration.Value is System.Collections.IEnumerable sequence && declaration.Value is not string &&
                sequence.Cast<object?>().All(value => ScalarValue.TryCreate(value, out _))))
                throw new InvalidOperationException("An explicit scalar enum must also be represented in the value domain's allowed values.");
            return constraints;
        }
        if (declarations.Length > 1) throw new InvalidOperationException("Multiple enum constraints conflict with the value domain.");
        if (declarations.Length == 1) ValidateEnumDeclaration(declarations[0], AllowedValues);
        return Array.AsReadOnly(constraints.Where(value => !value.Kind.Equals("enum", StringComparison.OrdinalIgnoreCase))
            .Append(new DataConstraint("enum", Array.AsReadOnly(AllowedValues.Select(value => value.Value).ToArray()))).ToArray());
    }
    private static void ValidateEnumDeclaration(DataConstraint declaration, IReadOnlyList<ScalarValue> expected)
    {
        if (declaration.Value is not System.Collections.IEnumerable sequence || declaration.Value is string)
            throw new InvalidOperationException("The enum constraint must be a sequence matching the value domain.");
        var values = new List<ScalarValue>();
        foreach (var item in sequence)
        {
            if (!ScalarValue.TryCreate(item, out var scalar)) throw new InvalidOperationException("The enum contains a value outside the scalar domain.");
            values.Add(scalar);
        }
        if (!values.SequenceEqual(expected)) throw new InvalidOperationException("The enum constraint conflicts with the value domain membership or sequence.");
    }
}

public enum PackageLocatorKind { DescriptorUri, RegistryName }
public sealed record PackageLocator
{
    public PackageLocatorKind Kind { get; }
    public string Value { get; }
    public PackageLocator(PackageLocatorKind kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == PackageLocatorKind.DescriptorUri && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new ArgumentException("A descriptor locator must be an HTTP(S) URL.", nameof(value));
        (Kind, Value) = (kind, value);
    }
}
public enum ValueEncoding { Value, Label }
public sealed record ValueDomainReference
{
    public string ResourceName { get; }
    public PackageLocator? Package { get; }
    public ValueEncoding? EncodedAs { get; }
    public ValueEncoding EffectiveEncoding => EncodedAs ?? ValueEncoding.Value;
    public string? BoundAssetId { get; }
    public ValueDomainReference(string resourceName, PackageLocator? package = null, ValueEncoding? encodedAs = null, string? boundAssetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        if (boundAssetId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(boundAssetId);
        if (encodedAs is not null && !Enum.IsDefined(encodedAs.Value)) throw new ArgumentOutOfRangeException(nameof(encodedAs));
        if (package is not null && boundAssetId is not null) throw new ArgumentException("An external reference cannot be bound to a same-package asset.");
        (ResourceName, Package, EncodedAs, BoundAssetId) = (resourceName, package, encodedAs, boundAssetId);
    }
}
public sealed record FieldPath
{
    public IReadOnlyList<string> Segments { get; }
    public FieldPath(params string[] segments)
    {
        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A field path requires logical name segments.", nameof(segments));
        Segments = Array.AsReadOnly(segments.ToArray());
    }
}
public sealed record CategoryTableDefinition
{
    public FieldPath ValueField { get; }
    public FieldPath? LabelField { get; }
    public bool? Ordered { get; }
    public ExtensionMetadata Extensions { get; }
    public CategoryTableDefinition(FieldPath valueField, FieldPath? labelField = null, bool? ordered = null, ExtensionMetadata? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(valueField);
        (ValueField, LabelField, Ordered, Extensions) = (valueField, labelField, ordered, MetadataSnapshot.CopyExtensions(extensions));
    }
}
