using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Registering;
using Moq;
using NUnit.Framework;
using Packata.Core.PathHandling;
using Packata.Core.ResourceReading;
using Packata.Core.Testing.PathHandling;
using PocketCsvReader;

namespace Packata.Core.Testing.ResourceReading;
public class TableDatabaseReaderBuilderTests
{
    private class FakeDbProviderFactory : DbProviderFactory
    {
        public static readonly DbProviderFactory Instance = new FakeDbProviderFactory();
    }

    [Test]
    public void Configure_IProviderFactoriesDiscoverer_Executed()
    {
        var resource = new Resource
        {
            Connection = new LiteralConnectionUrl("mssql://server/database"),
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDatabaseDialect() { Table = "Customer" }
        };

        var discoverMock = new Mock<IProviderFactoriesDiscoverer>();
        discoverMock.Setup(x => x.Execute()).Returns(new[] { typeof(FakeDbProviderFactory) });

        var builder = new TableDatabaseReaderBuilder(new ProviderFactoriesRegistrator(discoverMock.Object));
        builder.Configure(resource);
        var reader = builder.Build();

        discoverMock.Verify(x => x.Execute(), Times.Once);
        Assert.That(reader, Is.Not.Null);
        Assert.That(reader, Is.InstanceOf<TableDatabaseReader>());
    }
}
