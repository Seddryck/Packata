using Moq;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class PriceSerializationTests
{
    [Test]
    public void Deserialize_Price_PreservesV32Fields()
    {
        const string yaml = """
            apiVersion: v3.2.0
            kind: DataContract
            id: pricing
            version: 1.0.0
            status: active
            price:
              priceAmount: 9.95
              priceCurrency: USD
              priceUnit: megabyte
            """;
        var serializer = new DataContractSerializer();

        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var contract = serializer.Deserialize(reader, Mock.Of<IDocumentContainer>(), new StorageProvider());

        Assert.Multiple(() =>
        {
            Assert.That(contract.Price, Is.Not.Null);
            Assert.That(contract.Price!.PriceAmount, Is.EqualTo(9.95m));
            Assert.That(contract.Price.PriceCurrency, Is.EqualTo("USD"));
            Assert.That(contract.Price.PriceUnit, Is.EqualTo("megabyte"));
        });
    }
}
