using System.Text;
using NUnit.Framework;
using Packata.OpenDataContract.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class ConsumerFacetMappingTests
{
    [Test]
    public void Mapper_preserves_declared_facets_and_missing_nullability()
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
        Assert.That(fields[0].Length, Is.EqualTo(50));
        Assert.That(fields[1].Nullable, Is.True);
        Assert.That(fields[1].Precision, Is.EqualTo(18));
        Assert.That(fields[1].Scale, Is.EqualTo(2));
        Assert.That(fields[2].Nullable, Is.Null);
    }
}
