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
    public void ReportDiagnostics_ReportsEveryDiagnosticAndReturnsSameResult()
    {
        var diagnostics = new[]
        {
            new MappingDiagnostic("TEST001", MappingSeverity.Information, "$", "Information"),
            new MappingDiagnostic("TEST002", MappingSeverity.Warning, "$", "Warning")
        };
        var result = new MappingResult<DataContract>(EmptyContract(), diagnostics);
        var reported = new List<MappingDiagnostic>();

        var returned = result.ReportDiagnostics(reported.Add);

        Assert.Multiple(() =>
        {
            Assert.That(returned, Is.SameAs(result));
            Assert.That(reported, Is.EqualTo(diagnostics));
        });
    }

    [Test]
    public void ThrowOnErrors_WithError_ThrowsCanonicalMappingException()
    {
        var diagnostic = new MappingDiagnostic("TEST003", MappingSeverity.Error, "$", "Invalid source");
        var result = new MappingResult<DataContract>(EmptyContract(), [diagnostic]);

        var exception = Assert.Throws<CanonicalMappingException>(() => result.ThrowOnErrors());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Diagnostics, Is.EqualTo(new[] { diagnostic }));
            Assert.That(exception.Message, Does.Contain("TEST003: Invalid source"));
        });
    }

    [Test]
    public void RequireValue_WithSuccessfulResult_ReturnsValue()
    {
        var contract = EmptyContract();

        var value = MappingResult<DataContract>.Success(contract).RequireValue();

        Assert.That(value, Is.SameAs(contract));
    }

    [Test]
    public void RequireValue_WithoutValue_ThrowsCanonicalMappingException()
    {
        var result = MappingResult<DataContract>.Failure(
            new MappingDiagnostic("TEST004", MappingSeverity.Warning, "$", "No value"));

        var exception = Assert.Throws<CanonicalMappingException>(() => result.RequireValue());

        Assert.That(exception!.Diagnostics, Has.Count.EqualTo(1));
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
