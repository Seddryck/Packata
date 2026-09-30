using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.Validation;

namespace Packata.Core.Testing.Validation;
public class LicenseValidatorTests
{
    [Test]
    public void IsValid_WithNullNameAndPath_ReturnsFalse()
    {
        var validator = new LicenseValidator();
        var license = new License() { Title = "my-license" };
        Assert.That(validator.IsValid(license), Is.False);
    }

    [Test]
    [TestCase(null, "https://license.org/my-license.html")]
    [TestCase("my-license", null)]
    [TestCase("my-license", "https://license.org/my-license.html")]
    public void IsValid_WithNullOrPathOrBoth_ReturnsTrue(string? name, string? path)
    {
        var validator = new LicenseValidator();
        var license = new License() { Title = "my-license" };
        Assert.That(validator.IsValid(license), Is.False);
    }

    [Test]
    [TestCase("my$license")]
    public void IsValid_WithName_ReturnsFalse(string name)
    {
        var validator = new LicenseValidator();
        var license = new License() { Name = name, Title = "my-license" };
        Assert.That(validator.IsValid(license), Is.False);
    }

    [Test]
    [TestCase("ODC-PDDL")]
    [TestCase("ODC-PDDL-1.0")]
    [TestCase("CC0-1.0")]
    public void IsValid_WithName_ReturnsTrue(string name)
    {
        var validator = new LicenseValidator();
        var license = new License() { Name = name, Title = "my-license" };
        Assert.That(validator.IsValid(license), Is.True);
    }

    [Test]
    [TestCase("/my-license.html")]
    [TestCase("/../my-license.html")]
    [TestCase("./my-license.html")]
    [TestCase("~/my-license.html")]
    [TestCase("licenses/../my-license.html")]
    [TestCase("file://folder/my-license.html")]
    [TestCase("sftp://license.org/my-license.html")]
    public void IsValid_WithPath_ReturnsFalse(string path)
    {
        var validator = new LicenseValidator();
        var license = new License() { Path = path, Title = "my-license" };
        Assert.That(validator.IsValid(license), Is.False);
    }

    [Test]
    [TestCase("https://license.org/my-license.html")]
    [TestCase("http://license.org/my-license.html")]
    [TestCase("ftps://license.org/my-license.html")]
    [TestCase("ftp://license.org/my-license.html")]
    [TestCase("my-license.html")]
    [TestCase("licenses/my-license.html")]
    public void IsValid_WithPath_ReturnsTrue(string path)
    {
        var validator = new LicenseValidator();
        var license = new License() { Path = path, Title = "my-license" };
        Assert.That(validator.IsValid(license), Is.True);
    }
}
