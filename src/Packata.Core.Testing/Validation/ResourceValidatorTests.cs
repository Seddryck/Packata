using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.Validation;

namespace Packata.Core.Testing.Validation;
public class ResourceValidatorTests
{
    [Test]
    [TestCase("any", "data.csv")]
    [TestCase("any", "data_1.csv", "data_2.csv")]
    [TestCase(null)]
    public void IsValid_SinglePropertySet_ReturnsFalse(object? data, params string[] paths)
    {
        var validator = new ResourceValidator();
        var resource = new Resource() {Data = data, Paths = [.. paths], Name = "my-resource"};
        Assert.That(validator.IsValid(resource), Is.False);
    }

    [Test]
    [TestCase("any")]
    [TestCase(null, "data_1.csv")]
    [TestCase(null, "data_1.csv", "data_2.csv")]
    public void IsValid_SinglePropertySet_ReturnsTrue(object? data, params string[] paths)
    {
        var validator = new ResourceValidator();
        var resource = new Resource() { Data = data, Paths = [.. paths], Name = "my-resource" };
        Assert.That(validator.IsValid(resource), Is.True);
    }

    [Test]
    [TestCase("data_1.csv")]
    [TestCase("data_1.csv", "data_2.csv")]
    [TestCase("https://data_1.csv")]
    [TestCase("https://data_1.csv", "https://data_2.csv")]
    public void IsValid_PathCoherent_ReturnsTrue(params string[] paths)
    {
        var validator = new ResourceValidator();
        var resource = new Resource() { Paths = [.. paths], Name = "my-resource" };
        Assert.That(validator.IsValid(resource), Is.True);
    }

    [Test]
    [TestCase("data_1.csv", "https://data_2.csv")]
    [TestCase("https://data_1.csv", "data_2.csv")]
    public void IsValid_PathCoherent_ReturnsFalse(params string[] paths)
    {
        var validator = new ResourceValidator();
        var resource = new Resource() { Paths = [.. paths], Name = "my-resource" };
        Assert.That(validator.IsValid(resource), Is.False);
    }

    [Test]
    [TestCase("data_1.csv")]
    [TestCase("data_1.csv", "data_2.csv")]
    [TestCase("https://data_1.csv")]
    [TestCase("https://data_1.csv", "https://data_2.csv")]
    public void IsValid_PathValid_ReturnsTrue(params string[] paths)
    {
        var validator = new ResourceValidator();
        var resource = new Resource() { Paths = [.. paths], Name = "my-resource" };
        Assert.That(validator.IsValid(resource), Is.True);
    }

    [Test]
    [TestCase("file://data_1.csv")]
    [TestCase("https://data_1.csv", "file://data_2.csv")]
    public void IsValid_PathValid_ReturnsFalse(params string[] paths)
    {
        var validator = new ResourceValidator();
        var resource = new Resource() { Paths = [.. paths], Name = "my-resource" };
        Assert.That(validator.IsValid(resource), Is.False);
    }

    [Test]
    public void IsValid_PathValid_ReturnsTrue()
    {
        var validator = new ResourceValidator();
        var resource = new Resource() {Name = "my-resource", Paths=["data.csv"] };
        Assert.That(validator.IsValid(resource), Is.True);
    }

    [Test]
    public void IsValid_PathValid_ReturnsFalse()
    {
        var validator = new ResourceValidator();
        var resource = new Resource() { Paths = ["data.csv"] };
        Assert.That(validator.IsValid(resource), Is.False);
    }
}
