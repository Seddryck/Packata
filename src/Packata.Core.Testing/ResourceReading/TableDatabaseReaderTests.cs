using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chrononuensis;
using DubUrl;
using DubUrl.Mapping;
using Moq;
using NUnit.Framework;
using Packata.Core.PathHandling;
using Packata.Core.ResourceReading;
using Packata.Core.Testing.PathHandling;
using PocketCsvReader;
using PocketCsvReader.Configuration;
using RichardSzalay.MockHttp;

namespace Packata.Core.Testing.ResourceReading;
public class TableDatabaseReaderTests
{
    [Test]
    public void ToDataReader_TableDefined_ExpectedCommand()
    {
        var resource = new Resource
        {
            Connection = new LiteralConnectionUrl("mssql://server/database"),
            Type = "table",
            Name = "my-resource",
            Dialect = new TableDatabaseDialect() { Table = "Customer" },
        };
        var mockCommand = new Mock<IDbCommand>();
        mockCommand.Setup(x => x.ExecuteReader()).Returns(Mock.Of<DbDataReader>);
        mockCommand.SetupSet(x => x.CommandText = It.IsAny<string>()); ;

        var mockConnection = new Mock<IDbConnection>();
        mockConnection.Setup(x => x.CreateCommand()).Returns(mockCommand.Object);

        var mockConnectionUrl = new Mock<ConnectionUrl>(It.IsAny<string>());
        mockConnectionUrl.Setup(x => x.Open()).Returns(mockConnection.Object);

        var mockFactory = new Mock<ConnectionUrlFactory>(new SchemeMapperBuilder());
        mockFactory.Setup(x => x.Instantiate(It.IsAny<string>())).Returns(mockConnectionUrl.Object);

        var reader = new TableDatabaseReader(mockFactory.Object);
        var dataReader = reader.ToDataReader(resource);

        Assert.That(dataReader, Is.Not.Null);
        mockCommand.Verify(x => x.ExecuteReader(), Times.Once);
        mockCommand.VerifySet(x => x.CommandText = "SELECT * FROM Customer", Times.Once);
        mockConnection.VerifyAll();
        mockConnectionUrl.VerifyAll();
        mockFactory.Verify(x => x.Instantiate("mssql://server/database"), Times.Once);
    }
}
