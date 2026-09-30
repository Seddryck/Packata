using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class MappingTests
{
    [Test]
    public void Success_WithWarning_RemainsSuccessful()
    {
        var contract = EmptyContract();
        var result = MappingResult<DataContract>.Success(
            contract,
            new MappingDiagnostic("TEST001", MappingSeverity.Warning, "schema[0]", "A value was preserved."));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Value, Is.SameAs(contract));
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Result_WithError_IsNotSuccessful()
    {
        var result = new MappingResult<DataContract>(
            EmptyContract(),
            [new MappingDiagnostic("TEST002", MappingSeverity.Error, "$", "The document cannot be mapped.")]);

        Assert.That(result.IsSuccessful, Is.False);
    }

    [Test]
    public void ExtensionMetadata_PreservesSourceNamespace()
    {
        var extensions = ExtensionMetadata.For(
            "odcs",
            new Dictionary<string, object?> { ["custom"] = 42 });

        Assert.That(extensions.TryGetNamespace("odcs", out var values), Is.True);
        Assert.That(values["custom"], Is.EqualTo(42));
    }

    private static DataContract EmptyContract()
        => new(
            new ContractIdentity("test"),
            new ContractMetadata(),
            [],
            [],
            new ContractGovernance());
}
