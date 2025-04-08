using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl;
using DubUrl.Schema;
using Moq;
using Core = Packata.Core;
using Packata.Provisioners.Database;
using YamlDotNet.Serialization.Schemas;
using NUnit.Framework;
using DubUrl.Querying.Dialects;
using DubUrl.Querying.TypeMapping;
using DubUrl.BulkCopy;
using System.Data;
using Packata.Core.ResourceReading;
using Packata.ResourceReaders;
using DubUrl.Querying.Dialects.Functions;

namespace Packata.Provisioners.Testing.Database;
public class DubUrlProvisionerTests
{
    [Test]
    public void DeploySchema_Default_CallScriptRendererAndDeployer()
    {
        var dataPackage = new Core.DataPackage()
        {
            Resources = [
                    new Core.Resource { Name = "Customer",
                        Schema = new Core.Schema()
                        { Fields =
                            [new Core.Field() { Name="CustomerId", Type="integer" }
                            , new Core.Field() { Name = "Fullname", Type = "string" }]
                        } },
                    new Core.Resource { Name = "Sales",
                        Schema = new Core.Schema()
                        { Fields =
                            [new Core.Field() { Name="SalesId", Type="integer" }
                            , new Core.Field() { Name = "Amount", Type = "number" }]
                        } }
                ]
        };

        var script = "CREATE TABLE";
        var typeMapper = new Mock<IDbTypeMapper>();
        var sqlFunctionMapper = new Mock<ISqlFunctionMapper>();
        var dialect = new Mock<IDialect>();
        dialect.Setup(x => x.DbTypeMapper).Returns(typeMapper.Object).Verifiable();
        dialect.Setup(x => x.SqlFunctionMapper).Returns(sqlFunctionMapper.Object).Verifiable();
        var connectionUrl = new Mock<ConnectionUrl>("mssql://./mydb");
        connectionUrl.Setup(x => x.Dialect).Returns(dialect.Object);
        var scriptRenderer = new Mock<SchemaScriptRenderer>(dialect.Object, SchemaCreationOptions.None);
        scriptRenderer.Setup(x => x.Render(It.IsAny<Schema>())).Returns(script).Verifiable();
        var schemaDeployer = new Mock<SchemaScriptDeployer>();
        schemaDeployer.Setup(x => x.DeploySchema(It.IsAny<ConnectionUrl>(), It.IsAny<string>())).Verifiable();

        var x = dialect.Object;
        var y = scriptRenderer.Object;

        var provisioner = new DubUrlProvisioner(connectionUrl.Object, scriptRenderer.Object, schemaDeployer.Object);
        provisioner.DeploySchema(dataPackage, new());

        dialect.VerifyAll();
        scriptRenderer.Verify(x => x.Render(It.IsAny<Schema>()), Times.Once);
        schemaDeployer.Verify(x => x.DeploySchema(connectionUrl.Object, script), Times.Once);
    }

    [Test]
    public void LoadData_Default_CallScriptRendererAndDeployer()
    {
        var dataPackage = new Core.DataPackage()
        {
            Resources = [
                    new Core.Resource { Name = "Customer",
                        Schema = new Core.Schema()
                        { Fields =
                            [new Core.Field() { Name="CustomerId", Type="integer" }
                            , new Core.Field() { Name = "Fullname", Type = "string" }]
                        } },
                    new Core.Resource { Name = "Sales",
                        Schema = new Core.Schema()
                        { Fields =
                            [new Core.Field() { Name="SalesId", Type="integer" }
                            , new Core.Field() { Name = "Amount", Type = "number" }]
                        } }
                ]
        };

        var dialect = new Mock<IDialect>();
        dialect.Setup(x => x.DbTypeMapper).Returns(new Mock<IDbTypeMapper>().Object).Verifiable();
        dialect.Setup(x => x.SqlFunctionMapper).Returns(new Mock<ISqlFunctionMapper>().Object).Verifiable();
        var connectionUrl = new Mock<ConnectionUrl>("mssql://./mydb");
        connectionUrl.Setup(x => x.Dialect).Returns(dialect.Object);
        var bulkCopyEngine = new Mock<IBulkCopyEngine>();
        bulkCopyEngine.Setup(x => x.Write(It.IsAny<string>(), It.IsAny<IDataReader>())).Verifiable();
        var bulkCopyEngineFactory = new Mock<BulkCopyEngineFactory>();
        bulkCopyEngineFactory.Setup(x => x.Create(It.IsAny<ConnectionUrl>())).Returns(bulkCopyEngine.Object).Verifiable();
        var resourceReader = new Mock<IResourceReader>();
        resourceReader.Setup(x => x.ToDataReader(It.IsAny<Core.Resource>())).Returns(new Mock<IDataReader>().Object).Verifiable();
        var resourceReaderFactory = new Mock<ResourceReaderFactory>();
        resourceReaderFactory.Setup(x => x.Create(It.IsAny<Core.Resource>())).Returns(resourceReader.Object).Verifiable();


        var provisioner = new DubUrlProvisioner(connectionUrl.Object, null, null, null, bulkCopyEngineFactory.Object, resourceReaderFactory.Object);
        provisioner.LoadData(dataPackage);

        bulkCopyEngineFactory.Verify(x => x.Create(connectionUrl.Object));
        bulkCopyEngine.Verify(x => x.Write("Customer", It.IsAny<IDataReader>()), Times.Once);
        bulkCopyEngine.Verify(x => x.Write("Sales", It.IsAny<IDataReader>()), Times.Once);
    }
}
