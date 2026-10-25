using System.Globalization;
using Moq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Storage;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Mapping;

public class ServiceCommitmentMapperTests
{
    private static MappingResult<Packata.Core.Contracts.DataContract> Map(params ServiceLevelAgreementProperty[] properties)
        => new OpenDataContractMapper().Map(new DataContract { Id = "test", SlaProperties = [.. properties] });

    private static ServiceLevelAgreementProperty Property(string name, object value, string? unit = null)
        => new() { Property = name, Value = value, Unit = unit };

    [TestCase("freshness")]
    [TestCase("latency")]
    [TestCase("frequency")]
    [TestCase("retention")]
    public void Equivalent_duration_units_produce_equal_values(string property)
    {
        var result = Map(Property(property, 120, "minutes"), Property(property, 2, "hours"), Property(property, 7200, "seconds"));
        var sla = result.RequireValue().Sla!;
        var commitments = property switch
        {
            "freshness" => sla.Freshness, "latency" => sla.Latency,
            "frequency" => sla.Frequency, _ => sla.Retention
        };
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(commitments.Select(item => item.Duration).Distinct().ToArray(), Has.Length.EqualTo(1));
            Assert.That(commitments[0].Duration, Is.EqualTo(new ContractDuration(0, TimeSpan.FromHours(2))));
        });
    }

    [Test]
    public void Calendar_years_normalize_to_months_without_assuming_a_day_count()
    {
        var sla = Map(Property("retention", 2, "years"), Property("retention", 24, "months")).RequireValue().Sla!;
        Assert.That(sla.Retention.Select(item => item.Duration), Is.All.EqualTo(new ContractDuration(24, TimeSpan.Zero)));
    }

    [TestCase("generalAvailability")]
    [TestCase("endOfSupport")]
    [TestCase("endOfLife")]
    public void Lifecycle_offsets_normalize_to_the_same_utc_instant(string property)
    {
        Lifecycle Read(object value) => Map(Property(property, value)).RequireValue().Lifecycle!;
        Assert.That(Read("2028-01-01T01:00:00+01:00"), Is.EqualTo(Read("2028-01-01T00:00:00Z")));
        Assert.That(Read(new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero)), Is.EqualTo(Read("2028-01-01")));
        Assert.That(Read(new DateOnly(2028, 1, 1)), Is.EqualTo(Read("2028-01-01")));
        Assert.That(Map(Property(property, "2028-01-01")).RequireValue().Sla, Is.Null);
    }

    [Test]
    public void Availability_preserves_multiple_windows_targets_and_scopes()
    {
        var first = Property("timeOfAvailability", "09:00-08:00");
        first.ValueExt = "10:00-08:00";
        first.Element = "orders.created_at, customers.id";
        var second = Property("timeOfAvailability", "17:00Z");
        var result = Map(first, second, Property("availability", "99.9", "%"));
        var availability = result.RequireValue().Sla!.Availability!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(availability.Windows, Has.Count.EqualTo(2));
            Assert.That(availability.Windows[0].Start, Is.EqualTo(new TimeOnly(17, 0)));
            Assert.That(availability.Windows[0].End, Is.EqualTo(new TimeOnly(18, 0)));
            Assert.That(availability.Windows[0].UtcOffset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(availability.Windows[0].Start, Is.EqualTo(availability.Windows[1].Start));
            Assert.That(availability.Windows[0].Elements,
                Is.EqualTo(new[] { new ElementReference("orders", "created_at"), new ElementReference("customers", "id") }));
            Assert.That(availability.Target.Single().Percentage, Is.EqualTo(99.9m));
        });
    }

    [Test]
    public void Missing_commitments_remain_absent()
    {
        var empty = Map().RequireValue();
        var partial = Map(Property("latency", 0, "s")).RequireValue();
        Assert.Multiple(() =>
        {
            Assert.That(empty.Sla, Is.Null);
            Assert.That(empty.Lifecycle, Is.Null);
            Assert.That(partial.Sla!.Freshness, Is.Empty);
            Assert.That(partial.Sla.Availability, Is.Null);
            Assert.That(partial.Lifecycle, Is.Null);
        });
    }

    [TestCase("latency", "2", null)]
    [TestCase("latency", "2", "fortnights")]
    [TestCase("latency", "-1", "hours")]
    [TestCase("latency", "1e100", "hours")]
    [TestCase("retention", "0.5", "months")]
    [TestCase("availability", "101", null)]
    [TestCase("generalAvailability", "2028-01-01T01:00:00", null)]
    [TestCase("timeOfAvailability", "25:00Z", null)]
    public void Invalid_values_are_preserved_with_a_diagnostic(string name, string value, string? unit)
    {
        var source = Property(name, value, unit);
        var result = Map(source);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("ODCS005"));
            Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("slaProperties/0"));
            Assert.That(result.Value!.Sla, Is.Null);
            Assert.That(result.Value.Lifecycle, Is.Null);
            Assert.That(result.Value.Extensions["odcs"]["slaProperties"], Is.EqualTo(new[] { source }));
        });
    }

    [Test]
    public void Unknown_properties_are_preserved_without_becoming_commitments()
    {
        var result = Map(Property("customMetric", 10));
        Assert.Multiple(() =>
        {
            Assert.That(result.RequireValue().Sla, Is.Null);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("ODCS004"));
            Assert.That(result.Value!.Extensions["odcs"]["slaProperties"], Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Source_extensions_survive_alongside_unsupported_properties()
    {
        var source = new DataContract
        {
            Id = "test",
            Description = new Description
            {
                CustomProperties = [new CustomProperty { Property = "owner", Value = "sales" }]
            },
            SlaProperties = [Property("customMetric", 10)]
        };
        var contract = new OpenDataContractMapper().Map(source).RequireValue();
        Assert.Multiple(() =>
        {
            Assert.That(contract.Extensions["odcs"].ContainsKey("customProperties"), Is.True);
            Assert.That(contract.Extensions["odcs"].ContainsKey("slaProperties"), Is.True);
        });
    }

    [Test]
    public void Duplicate_milestones_and_extended_durations_are_not_silently_overwritten()
    {
        var duration = Property("frequency", 1, "d");
        duration.ValueExt = 2;
        var result = Map(Property("ga", "2028-01-01"), Property("ga", "2029-01-01"), duration);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.Count.EqualTo(2));
            Assert.That(result.RequireValue().Lifecycle!.GeneralAvailability,
                Is.EqualTo(new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(result.Value!.Sla, Is.Null);
        });
    }

    [Test]
    public void Serialized_yaml_normalizes_under_a_non_english_culture()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sla
            slaDefaultElement: orders.created_at
            schema:
              - name: orders
            slaProperties:
              - property: latency
                value: 1.5
                unit: hours
              - property: generalAvailability
                value: 2028-01-01T01:00:00+01:00
              - property: timeOfAvailability
                value: 09:00-08:00
            """;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-BE");
            using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
            var source = new DataContractSerializer().Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());
            var result = new OpenDataContractMapper().Map(source);
            Assert.Multiple(() =>
            {
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(result.RequireValue().Sla!.Latency.Single().Duration.Elapsed, Is.EqualTo(TimeSpan.FromMinutes(90)));
                Assert.That(result.Value!.Sla!.Latency.Single().Elements, Is.EqualTo(new[] { new ElementReference("orders", "created_at") }));
                Assert.That(result.Value.Lifecycle!.GeneralAvailability!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            });
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestCase("created_at", "orders", "created_at")]
    [TestCase("orders", "orders", null)]
    [TestCase("orders.address.city", "orders", "address.city")]
    [TestCase("schema/orders", "orders", null)]
    [TestCase("schema/orders/properties/created_at", "orders", "created_at")]
    [TestCase("schema/orders/properties/address/properties/city", "orders", "address.city")]
    public void Element_references_support_dataset_field_and_nested_paths(string element, string dataset, string? field)
    {
        var source = Property("latency", 1, "h");
        source.Element = element;
        var document = new DataContract { Id = "test", Schema = [new SchemaObject { Name = "orders" }], SlaProperties = [source] };
        var result = new OpenDataContractMapper().Map(document);
        Assert.That(result.RequireValue().Sla!.Latency.Single().Elements.Single(), Is.EqualTo(new ElementReference(dataset, field)));
    }

    [Test]
    public void Ambiguous_element_references_do_not_become_unscoped_commitments()
    {
        var source = Property("latency", 2, "h");
        source.Element = "created_at";
        var result = Map(source);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("ODCS005"));
            Assert.That(result.RequireValue().Sla, Is.Null);
        });
    }
}
