using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.OpenDataContract.Serialization;
using Packata.OpenDataContract.Serialization.Yaml;
using Packata.Core.Storage;
using Moq;
using Packata.OpenDataContract.Types;
using Packata.OpenDataContract.ServerTypes;

namespace Packata.OpenDataContract.Testing.Serialization;

public abstract class BaseDataContractSerializerTests
{
    protected abstract IDataContractSerializer GetSerializer();

    protected abstract string GetFormat();

    protected abstract Stream GetDataContractFundamentals();

    [Test]
    public void Deserialize_DataContractFundamentals_ReturnsDataContract()
    {
        using var stream = GetDataContractFundamentals();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.ApiVersion, Is.EqualTo("v3.0.2"));
            Assert.That(dataContract.Kind, Is.EqualTo("DataContract"));
            Assert.That(dataContract.Name, Is.EqualTo("seller_payments_v1"));
            Assert.That(dataContract.Id, Is.EqualTo("53581432-6c55-4ba2-a65f-72344a91553a"));
            Assert.That(dataContract.Version, Is.EqualTo("1.1.0"));
            Assert.That(dataContract.Status, Is.EqualTo("active"));
            Assert.That(dataContract.Domain, Is.EqualTo("seller"));
            Assert.That(dataContract.DataProduct, Is.EqualTo("payments"));
            Assert.That(dataContract.Tenant, Is.EqualTo("ClimateQuantumInc"));
        });
    }

    [Test]
    public void Deserialize_DataContractDescription_ReturnsDataContract()
    {
        using var stream = GetDataContractFundamentals();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Description, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Description.Purpose, Is.EqualTo("Views built on top of the seller tables."));
            Assert.That(dataContract.Description.Limitations, Is.EqualTo("None"));
            Assert.That(dataContract.Description.Usage, Is.EqualTo("Any"));
            Assert.That(dataContract.Description.AuthoritativeDefinitions, Has.Count.EqualTo(0));
            Assert.That(dataContract.Description.CustomProperties, Is.Not.Null);
            Assert.That(dataContract.Description.CustomProperties, Has.Count.EqualTo(2));
            Assert.That(dataContract.Description.CustomProperties.Keys, Does.Contain("somePropertyName"));
            Assert.That(dataContract.Description.CustomProperties["somePropertyName"], Is.EqualTo("property.value"));
            Assert.That(dataContract.Description.CustomProperties.Keys, Does.Contain("dataprocClusterName"));
            Assert.That(dataContract.Description.CustomProperties["dataprocClusterName"], Is.EqualTo(["cluster name"]));
        });
    }

    [Test]
    public void Deserialize_DataContractAuthoritativeDefinitions_ReturnsDataContract()
    {
        using var stream = GetDataContractFundamentals();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Description, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.AuthoritativeDefinitions, Has.Count.EqualTo(2));
            Assert.That(dataContract.AuthoritativeDefinitions[0].Url, Is.EqualTo("https://catalog.data.gov/dataset/air-quality"));
            Assert.That(dataContract.AuthoritativeDefinitions[0].Type, Is.EqualTo("businessDefinition"));
            Assert.That(dataContract.AuthoritativeDefinitions[1].Type, Is.EqualTo("videoTutorial"));
            Assert.That(dataContract.AuthoritativeDefinitions[1].Url, Is.EqualTo("https://youtu.be/jbY1BKFj9ec"));
        });
    }

    [Test]
    public void Deserialize_DataContractTags_ReturnsDataContract()
    {
        using var stream = GetDataContractFundamentals();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Description, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Tags, Has.Length.EqualTo(2));
            Assert.That(dataContract.Tags, Does.Contain("finance"));
            Assert.That(dataContract.Tags, Does.Contain("day-to-day"));
        });
    }

    protected abstract Stream GetDataContractSchema();

    [Test]
    public void Deserialize_DataContractSchema_ReturnsDataContract()
    {
        using var stream = GetDataContractSchema();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Schema, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Schema, Has.Count.EqualTo(1));
            Assert.That(dataContract.Schema[0].Name, Is.EqualTo("tbl"));
            Assert.That(dataContract.Schema[0].LogicalType, Is.EqualTo("object"));
            Assert.That(dataContract.Schema[0].PhysicalType, Is.EqualTo("table"));
            Assert.That(dataContract.Schema[0].PhysicalName, Is.EqualTo("tbl_1"));
            Assert.That(dataContract.Schema[0].Description, Is.EqualTo("Provides core payment metrics"));
            Assert.That(dataContract.Schema[0].AuthoritativeDefinitions, Has.Count.EqualTo(2));
            Assert.That(dataContract.Schema[0].Tags, Has.Count.EqualTo(1));
            Assert.That(dataContract.Schema[0].DataGranularityDescription, Is.EqualTo("Aggregation on columns txn_ref_dt, pmt_txn_id"));
        });
    }

    [Test]
    public void Deserialize_DataContractSchemaProperties_ReturnsDataContract()
    {
        using var stream = GetDataContractSchema();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Schema, Is.Not.Null);
        Assert.That(dataContract.Schema[0].Properties, Has.Count.EqualTo(3));

        var propRefDate = dataContract.Schema[0].Properties[0];
        Assert.Multiple(() =>
        {
            Assert.That(propRefDate.Name, Is.EqualTo("txn_ref_dt"));
            Assert.That(propRefDate.BusinessName, Is.EqualTo("transaction reference date"));
            Assert.That(propRefDate.LogicalType, Is.TypeOf<DateLogicalType>());
            var dateProp = (DateLogicalType)propRefDate.LogicalType!;
            Assert.That(dateProp.Format, Is.EqualTo("yyyy-MM-dd"));

            Assert.That(propRefDate.PhysicalType, Is.EqualTo("date"));
            Assert.That(propRefDate.Description, Is.Null);
            Assert.That(propRefDate.Partitioned, Is.True);
            Assert.That(propRefDate.PartitionKeyPosition, Is.EqualTo(1));
            Assert.That(propRefDate.CriticalDataElement, Is.False);
            Assert.That(propRefDate.Tags, Is.Empty);
            Assert.That(propRefDate.Classification, Is.EqualTo("public"));
            Assert.That(propRefDate.TransformSourceObjects, Has.Count.EqualTo(3));
            Assert.That(propRefDate.TransformLogic, Is.EqualTo("sel t1.txn_dt as txn_ref_dt from table_name_1 as t1, table_name_2 as t2, table_name_3 as t3 where t1.txn_dt=date-3"));
            Assert.That(propRefDate.TransformDescription, Is.EqualTo("Defines the logic in business terms."));
            Assert.That(propRefDate.Examples, Has.Count.EqualTo(2));
        });
    }

    protected abstract Stream GetDataContractSchemaTypedProps();

    [Test]
    public void Deserialize_DataContractSchemaTypedProps_ReturnsDataContract()
    {
        using var stream = GetDataContractSchemaTypedProps();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Schema, Is.Not.Null);
        Assert.That(dataContract.Schema, Has.Count.EqualTo(1));
        Assert.That(dataContract.Schema[0].Properties, Has.Count.EqualTo(5));
        var props = dataContract.Schema[0].Properties;

        Assert.Multiple(() =>
        {
            Assert.That(props[0].Name, Is.EqualTo("date_field"));
            Assert.That(props[0].LogicalType, Is.TypeOf<DateLogicalType>());

            var option = (DateLogicalType)props[0].LogicalType!;
            Assert.That(option.Format, Is.EqualTo("yyyy-MM-dd"));
            Assert.That(option.ExclusiveMinimum, Is.False);
            Assert.That(option.ExclusiveMaximum, Is.False);
            Assert.That(option.Minimum, Is.Null);
            Assert.That(option.Maximum, Is.Null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(props[1].Name, Is.EqualTo("integer_field"));
            Assert.That(props[1].LogicalType, Is.TypeOf<IntegerLogicalType>());

            var option = (IntegerLogicalType)props[1].LogicalType!;
            Assert.That(option.Format, Is.EqualTo("i64"));
            Assert.That(option.ExclusiveMinimum, Is.True);
            Assert.That(option.ExclusiveMaximum, Is.False);
            Assert.That(option.Minimum, Is.EqualTo(100));
            Assert.That(option.Maximum, Is.Null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(props[2].Name, Is.EqualTo("number_field"));
            Assert.That(props[2].LogicalType, Is.TypeOf<NumberLogicalType>());

            var option = (NumberLogicalType)props[2].LogicalType!;
            Assert.That(option.Format, Is.EqualTo("f32"));
            Assert.That(option.ExclusiveMinimum, Is.False);
            Assert.That(option.ExclusiveMaximum, Is.False);
            Assert.That(option.Minimum, Is.Null);
            Assert.That(option.Maximum, Is.Null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(props[3].Name, Is.EqualTo("boolean_field"));
            Assert.That(props[3].LogicalType, Is.TypeOf<BooleanLogicalType>());

            var option = (BooleanLogicalType)props[3].LogicalType!;
        });

        Assert.Multiple(() =>
        {
            Assert.That(props[4].Name, Is.EqualTo("string_field"));
            Assert.That(props[4].LogicalType, Is.TypeOf<StringLogicalType>());

            var option = (StringLogicalType)props[4].LogicalType!;
            Assert.That(option.Format, Is.EqualTo("email"));
            Assert.That(option.Pattern, Is.Null);
            Assert.That(option.MinLength, Is.EqualTo(10));
            Assert.That(option.MaxLength, Is.EqualTo(120));
        });
    }
    protected abstract Stream GetDataContractServers();

    [Test]
    public void Deserialize_DataContractServers_ReturnsDataContract()
    {
        using var stream = GetDataContractServers();
        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.That(dataContract.Servers, Is.Not.Null);
        Assert.That(dataContract.Servers, Has.Count.EqualTo(3));

        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Servers[0].Server, Is.EqualTo("sqlServer001"));
            Assert.That(dataContract.Servers[0].Type, Is.EqualTo("sqlserver"));
            Assert.That(dataContract.Servers[0].Environment, Is.EqualTo("prod"));

            Assert.That(dataContract.Servers[0], Is.TypeOf<MsSqlServer>());
            var mssql = (MsSqlServer)dataContract.Servers[0];
            Assert.That(mssql.Host, Is.EqualTo("192.80.80.1"));
            Assert.That(mssql.Port, Is.EqualTo(1433));
            Assert.That(mssql.Database, Is.EqualTo("crm"));
            Assert.That(mssql.Schema, Is.EqualTo("dbo"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Servers[1].Server, Is.EqualTo("duckDb002"));
            Assert.That(dataContract.Servers[1].Type, Is.EqualTo("duckdb"));
            Assert.That(dataContract.Servers[1].Environment, Is.EqualTo("prod"));

            Assert.That(dataContract.Servers[1], Is.TypeOf<DuckDbServer>());
            var duckdb = (DuckDbServer)dataContract.Servers[1];
            Assert.That(duckdb.Database, Is.EqualTo("./customers-analysis.duckdb"));
            Assert.That(duckdb.Schema, Is.EqualTo("cch"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Servers[2].Server, Is.EqualTo("azureEU"));
            Assert.That(dataContract.Servers[2].Type, Is.EqualTo("azure"));
            Assert.That(dataContract.Servers[2].Environment, Is.EqualTo("uat"));

            Assert.That(dataContract.Servers[2], Is.TypeOf<AzureServer>());
            var azure = (AzureServer)dataContract.Servers[2];
            Assert.That(azure.Location, Is.EqualTo("az://my-data-location"));
            Assert.That(azure.Format, Is.EqualTo("parquet"));
        });
    }

    [Test]
    public void Deserialize_AllDataTypes_CorrectDataTypes()
    {
        string resourceName = $"{GetType().Namespace}.Resources.all-data-types.odcs.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Name, Is.EqualTo("my_table"));
            Assert.That(dataContract.Id, Is.EqualTo("53581432-6c55-4ba2-a65f-72344a91553a"));
            Assert.That(dataContract.Version, Is.EqualTo("1.0.0"));
        });
        Assert.That(dataContract.Schema, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Schema, Has.Count.EqualTo(1));
            Assert.That(dataContract.Schema[0].Name, Is.EqualTo("transactions_tbl"));
            Assert.That(dataContract.Schema[0].Description, Is.EqualTo("Provides core payment metrics"));
            Assert.That(dataContract.Schema[0].DataGranularityDescription, Is.EqualTo("Aggregation on names txn_ref_dt, pmt_txn_id"));
            Assert.That(dataContract.Schema[0].PhysicalType, Is.EqualTo("table"));
        });

        Assert.That(dataContract.Schema[0].Properties, Is.Not.Null);
        var props = dataContract.Schema[0].Properties;

        Assert.Multiple(() =>
        {
            Assert.That(props[0].Name, Is.EqualTo("account_id"));
            Assert.That(props[0].PhysicalType, Is.EqualTo("string"));
            Assert.That(props[0].LogicalType, Is.TypeOf<StringLogicalType>());
            var stringProp = (StringLogicalType)props[0].LogicalType!;
            Assert.That(stringProp.MinLength, Is.EqualTo(11));
            Assert.That(stringProp.MaxLength, Is.EqualTo(11));
            Assert.That(stringProp.Pattern, Is.EqualTo("ACC[0-9]{8}"));

            Assert.That(props[1].Name, Is.EqualTo("txn_ref_date"));
            Assert.That(props[1].PhysicalType, Is.EqualTo("date"));
            Assert.That(props[1].LogicalType, Is.TypeOf<DateLogicalType>());
            var dateProp = (DateLogicalType)props[1].LogicalType!;
            Assert.That(dateProp.Minimum, Is.EqualTo(new DateTime(2020, 01, 01)));
            Assert.That(dateProp.Maximum, Is.EqualTo(new DateTime(2021, 01, 01)));
            Assert.That(dateProp.Format, Is.EqualTo("yyyy-MM-dd"));

            Assert.That(props[2].Name, Is.EqualTo("txn_timestamp"));
            Assert.That(props[2].PhysicalType, Is.EqualTo("timestamp"));
            Assert.That(props[2].LogicalType, Is.TypeOf<DateLogicalType>());
            var tsProp = (DateLogicalType)props[2].LogicalType!;
            Assert.That(tsProp.Minimum, Is.EqualTo(new DateTime(2020, 01, 01)));
            Assert.That(tsProp.Maximum, Is.EqualTo(new DateTime(2021, 01, 01)));
            Assert.That(tsProp.Format, Is.EqualTo("yyyy-MM-dd HH:mm:ss"));

            Assert.That(props[3].Name, Is.EqualTo("amount"));
            Assert.That(props[3].PhysicalType, Is.EqualTo("double"));
            Assert.That(props[3].LogicalType, Is.TypeOf<NumberLogicalType>());
            var numberProp = (NumberLogicalType)props[3].LogicalType!;
            Assert.That(numberProp.Minimum, Is.EqualTo(0));
            Assert.That(numberProp.Format, Is.EqualTo("f32"));

            Assert.That(props[4].Name, Is.EqualTo("age"));
            Assert.That(props[4].PhysicalType, Is.EqualTo("int"));
            Assert.That(props[4].LogicalType, Is.TypeOf<IntegerLogicalType>());
            var intProp = (IntegerLogicalType)props[4].LogicalType!;
            Assert.That(intProp.Minimum, Is.EqualTo(18));
            Assert.That(intProp.Maximum, Is.EqualTo(100));
            Assert.That(intProp.ExclusiveMaximum, Is.True);
            Assert.That(intProp.Format, Is.EqualTo("i64"));

            Assert.That(props[5].Name, Is.EqualTo("is_open"));
            Assert.That(props[5].PhysicalType, Is.EqualTo("bool"));
            Assert.That(props[5].LogicalType, Is.TypeOf<BooleanLogicalType>());

            Assert.That(props[6].Name, Is.EqualTo("latest_txns"));
            Assert.That(props[6].PhysicalType, Is.EqualTo("list"));
            Assert.That(props[6].LogicalType, Is.TypeOf<ArrayLogicalType>());
            var arrayProp = (ArrayLogicalType)props[6].LogicalType!;
            Assert.That(arrayProp.MinItems, Is.EqualTo(0));
            Assert.That(arrayProp.MaxItems, Is.EqualTo(3));
            Assert.That(arrayProp.UniqueItems, Is.True);

            Assert.That(props[7].Name, Is.EqualTo("customer_details"));
            Assert.That(props[7].PhysicalType, Is.EqualTo("json"));
            Assert.That(props[7].LogicalType, Is.TypeOf<ObjectLogicalType>());
            var objProp = (ObjectLogicalType)props[7].LogicalType!;
            Assert.That(objProp.MaxProperties, Is.EqualTo(5));
            Assert.That(objProp.Required, Has.Length.EqualTo(2));
            Assert.That(objProp.Required, Does.Contain("num_children"));
            Assert.That(objProp.Required, Does.Contain("date_of_birth"));
        });
    }
}
