using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Core.Validation;

namespace Packata.Core.Testing.Validation;
public class ContributorValidatorTests
{
    [Test]
    public void IsValid_AllPropertiesNull_ReturnsFalse()
    {
        var validator = new ContributorValidator();
        var Contributor = new Contributor() {};
        Assert.That(validator.IsValid(Contributor), Is.False);
    }

    [Test]
    public void IsValid_AtLeastAPropertySet_ReturnsTrue()
    {
        var validator = new ContributorValidator();
        var Contributor = new Contributor() { GivenName = "Hero" };
        Assert.That(validator.IsValid(Contributor), Is.True);
    }

    [Test]
    [TestCase("this-is-no-an-email")]
    public void IsValid_WithEmail_ReturnsFalse(string email)
    {
        var validator = new ContributorValidator();
        var Contributor = new Contributor() { Email = email };
        Assert.That(validator.IsValid(Contributor), Is.False);
    }

    [Test]
    [TestCase("john.doe@email.org")]
    public void IsValid_WithName_ReturnsTrue(string email)
    {
        var validator = new ContributorValidator();
        var Contributor = new Contributor() { Email = email };
        Assert.That(validator.IsValid(Contributor), Is.True);
    }

    [Test]
    [TestCase("/my-Contributor.html")]
    [TestCase("/../my-Contributor.html")]
    [TestCase("./my-Contributor.html")]
    [TestCase("~/my-Contributor.html")]
    [TestCase("Contributors/../my-Contributor.html")]
    [TestCase("file://folder/my-Contributor.html")]
    [TestCase("sftp://Contributor.org/my-Contributor.html")]
    public void IsValid_WithPath_ReturnsFalse(string path)
    {
        var validator = new ContributorValidator();
        var Contributor = new Contributor() { Path = path };
        Assert.That(validator.IsValid(Contributor), Is.False);
    }

    [Test]
    [TestCase("https://example.org/my-Contributor.html")]
    [TestCase("http://example.org/my-Contributor.html")]
    [TestCase("ftps://example.org/my-Contributor.html")]
    [TestCase("ftp://example.org/my-Contributor.html")]
    [TestCase("my-example.html")]
    [TestCase("example/my-Contributor.html")]
    public void IsValid_WithPath_ReturnsTrue(string path)
    {
        var validator = new ContributorValidator();
        var Contributor = new Contributor() { Path = path };
        Assert.That(validator.IsValid(Contributor), Is.True);
    }
}
