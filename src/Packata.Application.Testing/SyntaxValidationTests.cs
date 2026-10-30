using NUnit.Framework;
using Packata.Application.Validation;

namespace Packata.Application.Testing;

public sealed class SyntaxValidationTests
{
    [TestCase("resources: []")]
    [TestCase("{\"resources\":[]}")]
    [TestCase("apiVersion: unsupported\nkind: DataContract\nschema: still-editing\n")]
    [TestCase("resources:\n  - path: unavailable://never-read/data.csv\n")]
    public void SyntaxOnlyValidation_DoesNotRequireSchemaSemanticsOrNativeDeserialization(string text)
    {
        var result = DescriptorSyntaxValidation.Validate(text);
        Assert.That(result.Valid, Is.True);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Syntax!.Text, Is.EqualTo(text));
    }

    [TestCase("resources: []")]
    [TestCase("apiVersion: unsupported\nkind: DataContract\nschema: still-editing\n")]
    public void SyntaxSuccess_DoesNotImplyDescriptorValidity(string text)
    {
        Assert.That(DescriptorSyntaxValidation.Validate(text).Valid, Is.True);
        Assert.That(DescriptorValidation.Validate(text).Valid, Is.False);
    }

    [TestCase("{resources: []}")]
    [TestCase("{\"resources\":[],}")]
    [TestCase("{\"resources\":broken}")]
    public void JsonLikeInput_UsesStrictJsonPolicyRatherThanOnlyTheYamlParser(string text)
    {
        Assert.DoesNotThrow(() => ContractSyntax.Parse(text));
        var result = DescriptorSyntaxValidation.Validate(text);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("VAL_PARSE"));
        var report = DescriptorValidation.Validate(text);
        Assert.That(report.SyntaxDiagnostics, Is.EqualTo(result.Diagnostics));
        Assert.That(report.Layers.All(layer => !layer.Executed), Is.True);
    }

    [TestCase("schema: [")]
    [TestCase("schema: []\nschema: []")]
    [TestCase("---\na: b\n---\nc: d")]
    [TestCase("a: &cycle [*cycle]")]
    [TestCase("[]")]
    public void InvalidOrUnsafeMappings_AreReportedWithoutThrowing(string text)
    {
        var result = DescriptorSyntaxValidation.Validate(text);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("$"));
    }

    [Test]
    public void ParserFailure_PreservesNeutralLocationAndNeverEchoesSourceExcerpts()
    {
        const string text = "name: private-test-value\nschema: [";
        var syntax = DescriptorSyntaxValidation.Validate(text);
        var finding = syntax.Diagnostics.Single();
        Assert.That(finding.Message, Does.Not.Contain("private-test-value"));
        Assert.That(finding.Location, Is.Not.Null);
        Assert.That(finding.Location!.Line, Is.GreaterThanOrEqualTo(1));
        Assert.That(finding.Location.Column, Is.GreaterThanOrEqualTo(1));
        Assert.That(DescriptorValidation.Validate(text).SyntaxDiagnostics, Is.EqualTo(syntax.Diagnostics));
    }

    [Test]
    public void Cancellation_PrecedesSyntaxParsing()
    {
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => DescriptorSyntaxValidation.Validate("schema: [", token));
        Assert.Throws<OperationCanceledException>(() => DescriptorValidation.Validate("schema: [", cancellationToken: token));
    }

    [Test]
    public void LargeScalar_IsNotSubjectToAnEditorSpecificSizeLimit()
    {
        var result = DescriptorSyntaxValidation.Validate("description: " + new string('x', 4 * 1024 * 1024));
        Assert.That(result.Valid, Is.True);
    }
}
