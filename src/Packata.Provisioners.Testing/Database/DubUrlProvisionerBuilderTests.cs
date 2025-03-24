using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Packata.Provisioners.Database;

namespace Packata.Provisioners.Testing.Database;

public class DubUrlProvisionerBuilderTests
{
    [Test]
    public void Build_Local_Success()
    {
        var builder = new DubUrlProvisionerBuilder();
        var provisioner = builder.Using("mssql").Local().WithDatabase("mydb").Build();
        Assert.That(provisioner, Is.Not.Null);
        Assert.That(provisioner, Is.TypeOf<DubUrlProvisioner>());
        Assert.That(((DubUrlProvisioner)provisioner).ConnectionUrl.Url, Is.EqualTo("mssql://./mydb"));
    }

    [Test]
    public void Build_Remote_Success()
    {
        var builder = new DubUrlProvisionerBuilder();
        var provisioner = builder.Using("mssql").OnServer("123.123.123.123").WithDatabase("mydb").Build();
        Assert.That(provisioner, Is.Not.Null);
        Assert.That(provisioner, Is.TypeOf<DubUrlProvisioner>());
        Assert.That(((DubUrlProvisioner)provisioner).ConnectionUrl.Url, Is.EqualTo("mssql://123.123.123.123/mydb"));
    }

    [Test]
    public void Build_Memory_Success()
    {
        var builder = new DubUrlProvisionerBuilder();
        var provisioner = builder.Using("duck").InMemory().WithDatabase("mydb").Build();
        Assert.That(provisioner, Is.Not.Null);
        Assert.That(provisioner, Is.TypeOf<DubUrlProvisioner>());
        Assert.That(((DubUrlProvisioner)provisioner).ConnectionUrl.Url, Is.EqualTo("duck://memory/mydb"));
    }

    [Test]
    public void Build_Credentials_Success()
    {
        var builder = new DubUrlProvisionerBuilder();
        var provisioner = builder.Using("mssql")
            .OnServer("123.123.123.123")
            .WithDatabase("mydb")
            .WithCredentials("admin", "pass123")
            .Build();
        Assert.That(provisioner, Is.Not.Null);
        Assert.That(provisioner, Is.TypeOf<DubUrlProvisioner>());
        Assert.That(((DubUrlProvisioner)provisioner).ConnectionUrl.Url, Is.EqualTo("mssql://admin:pass123@123.123.123.123/mydb"));
    }
}
