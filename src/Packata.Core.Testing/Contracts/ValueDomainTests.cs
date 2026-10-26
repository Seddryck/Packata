using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class ValueDomainTests
{
    [TestCase("fr", true)]
    [TestCase("FR-be", true)]
    [TestCase("zh-Hant-TW", true)]
    [TestCase("en-US-u-ca-gregory", true)]
    [TestCase("de-x-company", true)]
    [TestCase("label_de", false)]
    [TestCase("fr-a", false)]
    [TestCase("fr-", false)]
    [TestCase(" fr", false)]
    [TestCase("123", false)]
    [TestCase(null, false)]
    public void Language_tags_validate_syntax_without_machine_locale_lookup(string? tag, bool valid)
        => Assert.That(LanguageTag.IsValid(tag), Is.EqualTo(valid));

    [Test]
    public void Alternate_label_paths_are_detached_and_read_only()
    {
        var paths = new List<FieldPath> { new("libelle") };
        var table = new CategoryTableDefinition(new("code"), labelFields: paths);
        paths.Clear();
        Assert.That(table.LabelFields.Single().Segments.Single(), Is.EqualTo("libelle"));
        Assert.That(() => ((IList<FieldPath>)table.LabelFields).Clear(), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Scalar_kinds_and_numbers_preserve_type_precision_and_range()
    {
        Assert.That(ScalarValue.From("001"), Is.Not.EqualTo(ScalarValue.From(1)));
        Assert.That(ScalarValue.From(1), Is.Not.EqualTo(ScalarValue.From(1m)));
        Assert.That(ScalarValue.From(0.1m), Is.EqualTo(ScalarValue.From(0.1d)));
        Assert.That(ScalarValue.From(0.1m).GetHashCode(), Is.EqualTo(ScalarValue.From(0.1d).GetHashCode()));
        Assert.That(ScalarValue.From(1e-200).Value, Is.EqualTo(1e-200));
        Assert.That(ScalarValue.From(decimal.MaxValue).Value, Is.EqualTo(decimal.MaxValue));
        Assert.That(ScalarValue.From(long.MaxValue).Value, Is.TypeOf<long>().And.EqualTo(long.MaxValue));
        foreach (var unsupported in new object[] { ulong.MaxValue, double.NaN, double.PositiveInfinity, new[] { 1, 2 }, new object() })
            Assert.That(ScalarValue.TryCreate(unsupported, out _), Is.False);
        using var json = JsonDocument.Parse("[1,1.0,0.1234567890123456789012345678,0.123456789012345678901234567890123456789]");
        Assert.That(ScalarValue.From(json.RootElement[0]).Kind, Is.EqualTo(ScalarKind.Integer));
        Assert.That(ScalarValue.From(json.RootElement[1]).Kind, Is.EqualTo(ScalarKind.Number));
        Assert.That(ScalarValue.From(json.RootElement[2]).Value, Is.EqualTo(0.1234567890123456789012345678m));
        Assert.That(ScalarValue.TryCreate(json.RootElement[3], out _), Is.False);
    }

    [Test]
    public void Domain_freezes_catalog_membership_and_metadata_without_equating_empty_and_unknown()
    {
        var mutable = new Dictionary<string, object?> { ["tags"] = new List<string> { "original" } };
        var entries = new List<ValueOption> { new(ScalarValue.From("A"), "Alpha", "", ExtensionMetadata.For("source", mutable)) };
        var allowed = new List<ScalarValue> { ScalarValue.From("A") };
        var domain = new ValueDomain(entries, allowed, ordered: false);
        entries.Clear(); allowed.Clear(); ((List<string>)mutable["tags"]!).Add("changed");
        Assert.That(domain.Entries!.Single().Label, Is.EqualTo("Alpha"));
        Assert.That(domain.Entries!.Single().Description, Is.EqualTo(""));
        Assert.That(domain.Entries!.Single().Extensions["source"]["tags"], Is.EqualTo(new[] { "original" }));
        Assert.That(domain.AllowedValues, Has.Count.EqualTo(1));
        var empty = new ValueDomain([], []);
        var referenced = new ValueDomain(reference: new("codes"));
        Assert.That(empty.Entries, Is.Empty); Assert.That(empty.AllowedValues, Is.Empty);
        Assert.That(referenced.Entries, Is.Null); Assert.That(referenced.AllowedValues, Is.Null);
        Assert.That(referenced.Reference!.EffectiveEncoding, Is.EqualTo(ValueEncoding.Value));
        Assert.That(referenced.Reference.EncodedAs, Is.Null);
    }

    [Test]
    public void Domain_enforces_catalog_subset_uniqueness_and_reference_invariants()
    {
        var a = new ValueOption(ScalarValue.From("A"));
        Assert.That(() => new ValueDomain(), Throws.ArgumentException);
        Assert.That(() => new ValueDomain([a], [a.Value], reference: new("codes")), Throws.ArgumentException);
        Assert.That(() => new ValueDomain([a]), Throws.ArgumentException);
        Assert.That(() => new ValueDomain([a, a], [a.Value]), Throws.ArgumentException);
        Assert.That(() => new ValueDomain([a], [ScalarValue.From("B")]), Throws.ArgumentException);
        Assert.That(() => new ValueDomain([a], [a.Value, a.Value]), Throws.ArgumentException);
        Assert.That(() => new ValueDomainReference("codes", new(PackageLocatorKind.RegistryName, "shared"), boundAssetId: "local"), Throws.ArgumentException);
        Assert.That(() => new FieldPath(), Throws.ArgumentException);
        Assert.That(() => new PackageLocator(PackageLocatorKind.DescriptorUri, "file:///codes"), Throws.ArgumentException);
    }

    [Test]
    public void Legacy_enum_is_derived_from_domain_and_conflicting_edits_are_rejected()
    {
        var a = ScalarValue.From("A");
        var domain = new ValueDomain([new(a, "Alpha")], [a]);
        var field = new DataField("code", "string", ValueDomain: domain);
        Assert.That(field.Constraints.Single().Value, Is.EqualTo(new[] { "A" }));
        Assert.That(() => ((IList<object?>)field.Constraints.Single().Value!).Add("B"), Throws.TypeOf<NotSupportedException>());
        var conflicting = field with { Constraints = [new("enum", new[] { "B" })] };
        Assert.That(() => conflicting.Constraints, Throws.InvalidOperationException);
        var unknown = new DataField("code", "string", Constraints: [new("enum", new[] { "A" })], ValueDomain: new(reference: new("codes")));
        Assert.That(() => unknown.Constraints, Throws.InvalidOperationException);
        var empty = new DataField("code", "string", ValueDomain: new([], []));
        Assert.That(empty.Constraints.Single().Value, Is.Empty);
    }
}
