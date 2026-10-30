using System.Text;
using NUnit.Framework;
using Packata.OpenDataContract.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class ConsumerFacetMappingTests
{
    [Test]
    public void Mapper_exposes_declared_temporal_parameters_without_inventing_defaults()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: temporal
            schema:
              - name: events
                properties:
                  - name: local
                    logicalType: timestamp
                    logicalTypeOptions:
                      timezone: false
                      precision: 9
                  - name: offset
                    logicalType: time
                    logicalTypeOptions:
                      timezone: true
                      defaultTimezone: Europe/Brussels
                  - name: unspecified
                    logicalType: timestamp
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var fields = new DataContractFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields;
        var local = Core.FieldTypeResolver.Resolve(fields[0], Core.TypeMappingOptions.Strict);
        var offset = Core.FieldTypeResolver.Resolve(fields[1], Core.TypeMappingOptions.Strict);
        var unspecified = Core.FieldTypeResolver.Resolve(fields[2], Core.TypeMappingOptions.Strict);
        Assert.That(local.Parameters.TemporalSemantics, Is.EqualTo(Core.TemporalSemantics.Local));
        Assert.That(local.Parameters.TemporalPrecision, Is.EqualTo(9));
        Assert.That(offset.Parameters.Timezone, Is.EqualTo("Europe/Brussels"));
        Assert.That(unspecified.Parameters.Timezone, Is.Null);
        Assert.That(unspecified.Parameters.TemporalPrecision, Is.Null);
    }

    [Test]
    public void Mapper_preserves_parameters_and_native_nullability_defaults()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sales
            schema:
              - name: orders
                properties:
                  - name: id
                    logicalType: string
                    required: true
                    logicalTypeOptions:
                      maxLength: 50
                  - name: amount
                    logicalType: number
                    required: false
                    logicalTypeOptions:
                      precision: 18
                      scale: 2
                  - name: unspecified
                    logicalType: string
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var fields = new DataContractFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields;
        Assert.That(fields[0].Nullable, Is.False);
        Assert.That(fields[0].Constraints.Single(x => x.Kind == "maxLength").Value, Is.EqualTo(50));
        Assert.That(fields[1].Nullable, Is.False);
        Assert.That(fields[1].Constraints.Single(x => x.Kind == "precision").Value, Is.EqualTo(18));
        Assert.That(fields[1].Constraints.Single(x => x.Kind == "scale").Value, Is.EqualTo(2));
        Assert.That(fields[2].Nullable, Is.False);
    }
}
