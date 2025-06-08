using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.OpenDataContract.Serialization;
using Packata.OpenDataContract.Serialization.Yaml;
using Packata.OpenDataContract.ServerTypes;
using Packata.OpenDataContract.Types;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public class DataContractSerializerTests : BaseDataContractSerializerTests
{
    protected override IDataContractSerializer GetSerializer()
        => new DataContractSerializer();

    protected override string GetFormat() => "yaml";

    protected override Stream GetDataContractFundamentals()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                apiVersion: v3.0.2 # Standard version
                kind: DataContract

                id: 53581432-6c55-4ba2-a65f-72344a91553a
                name: seller_payments_v1
                version: 1.1.0 # Data Contract Version 
                status: active
                domain: seller
                dataProduct: payments
                tenant: ClimateQuantumInc

                authoritativeDefinitions: 
                - url: https://catalog.data.gov/dataset/air-quality 
                  type: businessDefinition
                - url: https://youtu.be/jbY1BKFj9ec
                  type: videoTutorial

                description:
                  purpose: Views built on top of the seller tables.
                  limitations: None
                  usage: Any
                  customProperties:
                  - property: somePropertyName
                    value: property.value
                  - property: dataprocClusterName # Used for specific applications like Elevate
                    value: [cluster name]

                tags: ['finance', 'day-to-day']
            "));

    protected override Stream GetDataContractSchema()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                schema:
                  - name: tbl
                    logicalType: object
                    physicalType: table
                    physicalName: tbl_1 
                    description: Provides core payment metrics 
                    authoritativeDefinitions: 
                      - url: https://catalog.data.gov/dataset/air-quality 
                        type: businessDefinition
                      - url: https://youtu.be/jbY1BKFj9ec
                        type: videoTutorial
                    tags: ['finance']
                    dataGranularityDescription: Aggregation on columns txn_ref_dt, pmt_txn_id
                    properties:
                      - name: txn_ref_dt
                        businessName: transaction reference date
                        logicalType: date
                        logicalTypeOptions:
                            format: yyyy-MM-dd
                        physicalType: date
                        description: null
                        partitioned: true
                        partitionKeyPosition: 1
                        criticalDataElement: false
                        tags: []
                        classification: public
                        transformSourceObjects:
                          - table_name_1
                          - table_name_2
                          - table_name_3
                        transformLogic: sel t1.txn_dt as txn_ref_dt from table_name_1 as t1, table_name_2 as t2, table_name_3 as t3 where t1.txn_dt=date-3
                        transformDescription: Defines the logic in business terms. 
                        examples:
                          - 2022-10-03
                          - 2020-01-28
                      - name: rcvr_id
                        primaryKey: true 
                        primaryKeyPosition: 1
                        businessName: receiver id
                        logicalType: string
                        physicalType: varchar(18)
                        required: false
                        description: A description for column rcvr_id.
                        partitioned: false
                        partitionKeyPosition: -1
                        criticalDataElement: false
                        tags: []
                        classification: restricted
                        encryptedName: enc_rcvr_id
                      - name: rcvr_cntry_code
                        primaryKey: false 
                        primaryKeyPosition: -1
                        businessName: receiver country code
                        logicalType: string
                        physicalType: varchar(2)
                        required: false
                        description: null
                        partitioned: false
                        partitionKeyPosition: -1
                        criticalDataElement: false
                        tags: []
                        classification: public
                        authoritativeDefinitions:
                          - url: https://collibra.com/asset/742b358f-71a5-4ab1-bda4-dcdba9418c25
                            type: businessDefinition
                          - url: https://github.com/myorg/myrepo
                            type: transformationImplementation
                          - url: jdbc:postgresql://localhost:5432/adventureworks/tbl_1/rcvr_cntry_code
                            type: implementation
                        encryptedName: rcvr_cntry_code_encrypted
            "));


    protected override Stream GetDataContractSchemaTypedProps()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                version: 1.0.0
                kind: DataContract
                id: 53581432-6c55-4ba2-a65f-72344a91553a
                status: active
                name: date_example
                apiVersion: v3.0.2
                schema:
                  - name: tbl_date_example
                    logicalType: object
                    physicalType: table
                    physicalName: tbl_date_example 
                    description: Provides date examples
                    customProperties:
                    - property: somePropertyName
                      value: property.value
                    - property: dataprocClusterName # Used for specific applications like Elevate
                      value: [cluster name]
                    properties:
                        - name: date_field
                          businessName: Date Field
                          logicalType: date
                          logicalTypeOptions:
                            format: yyyy-MM-dd
                          physicalType: date
                          description: A date field example.
                          partitioned: true
                          partitionKeyPosition: 1
                          criticalDataElement: false
                          tags: []
                          classification: public
                          transformSourceObjects:
                          - table_name_1
                          - table_name_2
                          - table_name_3
                          transformLogic: sel t1.date_field as date_field from table_name_1 as t1, table_name_2 as t2, table_name_3 as t3 where t1.date_field=date-3
                          transformDescription: Defines the logic in business terms.
                        - name: integer_field
                          logicalType: integer
                          logicalTypeOptions:
                            format: i64
                            exclusiveMinimum: true
                            minimum: 100
                        - name: number_field
                          logicalType: number
                          logicalTypeOptions:
                            format: f32
                        - name: boolean_field
                          logicalType: boolean
                        - name: string_field
                          logicalType: string
                          logicalTypeOptions:
                            format: email
                            minLength: 10
                            maxLength: 120
            "));

    protected override Stream GetDataContractServers()
        => new MemoryStream(Encoding.UTF8.GetBytes(@"
                version: 1.0.0
                kind: DataContract
                id: 53581432-6c55-4ba2-a65f-72344a91553a
                status: active
                name: date_example
                apiVersion: v3.0.2
                servers:
                  - server: sqlServer001
                    type: sqlserver
                    environment: prod
                    host: 192.80.80.1
                    database: crm
                    schema: dbo
                  - server: duckDb002
                    type: duckdb
                    environment: prod
                    database: ./customers-analysis.duckdb
                    schema: cch
                  - server: azureEU
                    type: azure
                    environment: uat
                    location: az://my-data-location
                    format: parquet
            "));

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

    [Test]
    public void Deserialize_KafkaServer_CorrectServer()
    {
        string resourceName = $"{GetType().Namespace}.Resources.kafka-server.odcs.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());
        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Id, Is.EqualTo("abc123"));
            Assert.That(dataContract.Status, Is.EqualTo("in development"));
        });

        Assert.That(dataContract.Schema, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Schema, Has.Count.EqualTo(1));
            Assert.That(dataContract.Schema[0].Name, Is.EqualTo("Orders"));
            Assert.That(dataContract.Schema[0].PhysicalName, Is.EqualTo("orders.avro.v1"));
            Assert.That(dataContract.Schema[0].PhysicalType, Is.EqualTo("topic"));
        });

        Assert.That(dataContract.Servers, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.Servers, Has.Count.EqualTo(1));
            Assert.That(dataContract.Servers[0].Server, Is.EqualTo("my-kafka"));
            Assert.That(dataContract.Servers[0].Type, Is.EqualTo("kafka"));
            Assert.That(dataContract.Servers[0], Is.TypeOf<KafkaServer>());
            var kafkaSrv = (KafkaServer)dataContract.Servers[0]!;
            Assert.That(kafkaSrv.Host, Is.EqualTo("pkc-7xoy1.eu-central-1.aws.confluent.cloud:9092"));
            Assert.That(kafkaSrv.Format, Is.EqualTo("avro"));
        });
    }

    [Test]
    public void Deserialize_FullKafkaDataContract_AllFieldsCorrect()
    {
        string resourceName = $"{GetType().Namespace}.Resources.kafka-schema.odcs.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.ApiVersion, Is.EqualTo("v3.0.2"));
            Assert.That(dataContract.Kind, Is.EqualTo("DataContract"));
            Assert.That(dataContract.Id, Is.EqualTo("orders"));
            Assert.That(dataContract.Name, Is.EqualTo("Orders Event Stream"));
            Assert.That(dataContract.Version, Is.EqualTo("0.0.1"));
            Assert.That(dataContract.Status, Is.EqualTo("development"));
        });

        Assert.That(dataContract.Schema, Is.Not.Null.And.Count.EqualTo(1));
        var schema = dataContract.Schema[0];
        Assert.Multiple(() =>
        {
            Assert.That(schema.Name, Is.EqualTo("Orders"));
            Assert.That(schema.PhysicalName, Is.EqualTo("orders"));
            Assert.That(schema.LogicalType, Is.EqualTo("object"));
            Assert.That(schema.PhysicalType, Is.EqualTo("topic"));
            Assert.That(schema.Description, Is.EqualTo("Orders Kafka topic"));
        });

        Assert.That(schema.Properties, Is.Not.Null.And.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            var custId = schema.Properties[0];
            Assert.That(custId.Name, Is.EqualTo("cust_id"));
            Assert.That(custId.BusinessName, Is.EqualTo("Customer ID"));
            Assert.That(custId.LogicalType, Is.TypeOf<StringLogicalType>());
            Assert.That(custId.PhysicalType, Is.EqualTo("string"));
            Assert.That(custId.Required, Is.True);

            var prodId = schema.Properties[1];
            Assert.That(prodId.Name, Is.EqualTo("prod_id"));
            Assert.That(prodId.BusinessName, Is.EqualTo("Product ID"));
            Assert.That(prodId.LogicalType, Is.TypeOf<StringLogicalType>());
            Assert.That(prodId.PhysicalType, Is.EqualTo("string"));
            Assert.That(prodId.Required, Is.True);

            var qty = schema.Properties[2];
            Assert.That(qty.Name, Is.EqualTo("qty"));
            Assert.That(qty.BusinessName, Is.EqualTo("Quantity"));
            Assert.That(qty.LogicalType, Is.TypeOf<IntegerLogicalType>());
            Assert.That(qty.PhysicalType, Is.EqualTo("int"));
            Assert.That(qty.Required, Is.True);
        });

        Assert.That(dataContract.Servers, Is.Not.Null.And.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            var server = dataContract.Servers[0];
            Assert.That(server.Server, Is.EqualTo("my-kafka"));
            Assert.That(server.Type, Is.EqualTo("kafka"));
            Assert.That(server, Is.TypeOf<KafkaServer>());

            var kafka = (KafkaServer)server!;
            Assert.That(kafka.Format, Is.EqualTo("avro"));
            Assert.That(kafka.Host, Is.EqualTo("kafkabroker1:9092"));
        });
    }

    [Test]
    public void Deserialize_KafkaDataContractWithAuthorityDefinition_AllFieldsCorrect()
    {
        string resourceName = $"{GetType().Namespace}.Resources.kafka-schemaregistry.odcs.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.ApiVersion, Is.EqualTo("v3.0.2"));
            Assert.That(dataContract.Kind, Is.EqualTo("DataContract"));
            Assert.That(dataContract.Id, Is.EqualTo("orders"));
            Assert.That(dataContract.Name, Is.EqualTo("Orders Event Stream"));
            Assert.That(dataContract.Version, Is.EqualTo("0.0.1"));
            Assert.That(dataContract.Status, Is.EqualTo("production"));
        });

        Assert.That(dataContract.Schema, Is.Not.Null.And.Count.EqualTo(1));
        var schema = dataContract.Schema[0];
        Assert.Multiple(() =>
        {
            Assert.That(schema.Name, Is.EqualTo("Orders"));
            Assert.That(schema.PhysicalName, Is.EqualTo("orders"));
            Assert.That(schema.LogicalType, Is.EqualTo("object"));
            Assert.That(schema.PhysicalType, Is.EqualTo("topic"));
            Assert.That(schema.Description, Is.EqualTo("Orders Kafka topic"));
        });

        Assert.That(schema.AuthoritativeDefinitions, Is.Not.Null.And.Count.EqualTo(1));
        var authority = schema.AuthoritativeDefinitions[0];
        Assert.Multiple(() =>
        {
            Assert.That(authority.Url, Is.EqualTo("https://schema-registry:8081"));
            Assert.That(authority.Type, Is.EqualTo("implementation"));
        });

        Assert.That(dataContract.Servers, Is.Not.Null.And.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            var server = dataContract.Servers[0];
            Assert.That(server.Server, Is.EqualTo("my-kafka"));
            Assert.That(server.Type, Is.EqualTo("kafka"));
            Assert.That(server, Is.TypeOf<KafkaServer>());

            var kafka = (KafkaServer)server!;
            Assert.That(kafka.Format, Is.EqualTo("avro"));
            Assert.That(kafka.Host, Is.EqualTo("kafkabroker1:9092"));
        });
    }

    [Test]
    public void Deserialize_TableContractWithKeysAndPartitions_AllFieldsCorrect()
    {
        string resourceName = $"{GetType().Namespace}.Resources.table-columns-with-partition.odcs.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.ApiVersion, Is.EqualTo("v3.0.2"));
            Assert.That(dataContract.Version, Is.EqualTo("1.0.0"));
            Assert.That(dataContract.Kind, Is.EqualTo("DataContract"));
            Assert.That(dataContract.Id, Is.EqualTo("53581432-6c55-4ba2-a65f-72344a91553c"));
            Assert.That(dataContract.Name, Is.EqualTo("my_table"));
            Assert.That(dataContract.Status, Is.EqualTo("active"));
            Assert.That(dataContract.DataProduct, Is.EqualTo("my_quantum"));
        });

        Assert.That(dataContract.Schema, Is.Not.Null.And.Count.EqualTo(1));
        var schema = dataContract.Schema[0];
        Assert.Multiple(() =>
        {
            Assert.That(schema.Name, Is.EqualTo("tbl"));
            Assert.That(schema.PhysicalType, Is.EqualTo("table"));
        });

        Assert.That(schema.Properties, Is.Not.Null.And.Count.EqualTo(4));

        var rcvrCntry = schema.Properties[0];
        Assert.Multiple(() =>
        {
            Assert.That(rcvrCntry.Name, Is.EqualTo("rcvr_cntry_code"));
            Assert.That(rcvrCntry.BusinessName, Is.EqualTo("Receiver country code"));
            Assert.That(rcvrCntry.LogicalType, Is.TypeOf<StringLogicalType>());
            Assert.That(rcvrCntry.PhysicalType, Is.EqualTo("varchar(2)"));
            Assert.That(rcvrCntry.PrimaryKey, Is.True);
            Assert.That(rcvrCntry.PrimaryKeyPosition, Is.EqualTo(1));
            Assert.That(rcvrCntry.Partitioned, Is.True);
            Assert.That(rcvrCntry.PartitionKeyPosition, Is.EqualTo(1));
        });

        var rcvrId = schema.Properties[1];
        Assert.Multiple(() =>
        {
            Assert.That(rcvrId.Name, Is.EqualTo("rcvr_id"));
            Assert.That(rcvrId.BusinessName, Is.EqualTo("Receiver identification number"));
            Assert.That(rcvrId.LogicalType, Is.TypeOf<StringLogicalType>());
            Assert.That(rcvrId.PhysicalType, Is.EqualTo("varchar(20)"));
            Assert.That(rcvrId.PrimaryKey, Is.True);
            Assert.That(rcvrId.PrimaryKeyPosition, Is.EqualTo(2));
            Assert.That(rcvrId.Partitioned, Is.False);
        });

        var year = schema.Properties[2];
        Assert.Multiple(() =>
        {
            Assert.That(year.Name, Is.EqualTo("year"));
            Assert.That(year.BusinessName, Is.EqualTo("Year of transaction"));
            Assert.That(year.LogicalType, Is.TypeOf<IntegerLogicalType>());
            Assert.That(year.PhysicalType, Is.EqualTo("int"));
            Assert.That(year.PrimaryKey, Is.False);
            Assert.That(year.Partitioned, Is.True);
            Assert.That(year.PartitionKeyPosition, Is.EqualTo(2));
        });

        var amount = schema.Properties[3];
        Assert.Multiple(() =>
        {
            Assert.That(amount.Name, Is.EqualTo("amount"));
            Assert.That(amount.BusinessName, Is.EqualTo("Transaction amount"));
            Assert.That(amount.LogicalType, Is.TypeOf<NumberLogicalType>());
            Assert.That(amount.PhysicalType, Is.EqualTo("double"));
            Assert.That(amount.PrimaryKey, Is.False);
            Assert.That(amount.Partitioned, Is.False);
        });
    }

    [Test]
    public void Deserialize_TableContractWithDescriptionsAndExamples_AllFieldsCorrect()
    {
        string resourceName = $"{GetType().Namespace}.Resources.table-column-description.odcs.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataContract.ApiVersion, Is.EqualTo("v3.0.2"));
            Assert.That(dataContract.Version, Is.EqualTo("1.0.0"));
            Assert.That(dataContract.Kind, Is.EqualTo("DataContract"));
            Assert.That(dataContract.Id, Is.EqualTo("53581432-6c55-4ba2-a65f-72344a91553a"));
            Assert.That(dataContract.Name, Is.EqualTo("my_quantum"));
            Assert.That(dataContract.Status, Is.EqualTo("active"));
        });

        Assert.That(dataContract.Schema, Is.Not.Null.And.Count.EqualTo(1));
        var schema = dataContract.Schema[0];
        Assert.Multiple(() =>
        {
            Assert.That(schema.Name, Is.EqualTo("tbl"));
            Assert.That(schema.PhysicalType, Is.EqualTo("table"));
            Assert.That(schema.Description, Is.EqualTo("Provides core payment metrics"));
            Assert.That(schema.DataGranularityDescription, Is.EqualTo("Aggregation on columns txn_ref_dt, pmt_txn_id"));
        });

        Assert.That(schema.Properties, Is.Not.Null.And.Count.EqualTo(1));
        var txnRefDt = schema.Properties[0];
        Assert.Multiple(() =>
        {
            Assert.That(txnRefDt.Name, Is.EqualTo("txn_ref_dt"));
            Assert.That(txnRefDt.BusinessName, Is.EqualTo("Transaction reference date"));
            Assert.That(txnRefDt.LogicalType, Is.TypeOf<DateLogicalType>());
            Assert.That(txnRefDt.PhysicalType, Is.EqualTo("date"));
            Assert.That(txnRefDt.Description, Is.EqualTo("Reference date for the transaction. Use this date in reports and aggregation rather than txn_mystical_dt, as it is slightly too mystical."));
            Assert.That(txnRefDt.Examples, Is.Not.Null.And.Count.EqualTo(2));
            Assert.That(txnRefDt.Examples![0], Is.EqualTo("2022-10-03"));
            Assert.That(txnRefDt.Examples[1], Is.EqualTo("2025-01-28"));
        });
    }
}
