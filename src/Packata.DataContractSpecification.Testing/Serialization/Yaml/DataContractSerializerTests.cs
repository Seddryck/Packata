using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Castle.Core.Resource;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Storage;
using Packata.DataContractSpecification;
using Packata.DataContractSpecification.Serialization;
using Packata.DataContractSpecification.Serialization.Yaml;
using Packata.DataContractSpecification.ServerTypes;
using Packata.DataContractSpecification.Testing.Serialization;

namespace Packata.DataContractSpecification.Testing.Serialization.Yaml;

public class DataContractSerializerTests : BaseDataContractSerializerTests
{
    protected override IDataContractSerializer GetSerializer()
        => new DataContractSerializer();

    protected override string GetFormat() => "yaml";

    [Test]
    public void Deserialize_CovidCases_AllFields()
    {
        string resourceName = $"{GetType().Namespace}.Resources.covid-cases.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(dataContract.DataContractSpecification, Is.EqualTo("0.9.3"));
            Assert.That(dataContract.Id, Is.EqualTo("covid_cases"));

            Assert.That(dataContract.Info, Is.Not.Null);
            Assert.That(dataContract.Info!.Title, Is.EqualTo("COVID-19 cases"));
            Assert.That(dataContract.Info.Description, Is.EqualTo("Johns Hopkins University Consolidated data on COVID-19 cases, sourced from Enigma"));
            Assert.That(dataContract.Info.Version, Is.EqualTo("0.0.1"));
        });

        Assert.That(dataContract.Models, Is.Not.Null);
        Assert.That(dataContract.Models, Does.ContainKey("covid_cases"));

        var model = dataContract.Models["covid_cases"];

        Assert.Multiple(() =>
        {
            Assert.That(model.Description, Does.Contain("confirmed covid cases reported"));

            Assert.That(model.Fields, Is.Not.Null);
            Assert.That(model.Fields["fips"].Type, Is.EqualTo("string"));
            Assert.That(model.Fields["fips"].Description, Does.Contain("state and county"));

            Assert.That(model.Fields["admin2"].Type, Is.EqualTo("string"));
            Assert.That(model.Fields["province_state"].Type, Is.EqualTo("string"));
            Assert.That(model.Fields["country_region"].Type, Is.EqualTo("string"));

            Assert.That(model.Fields["last_update"].Type, Is.EqualTo("timestamp_ntz"));
            Assert.That(model.Fields["latitude"].Type, Is.EqualTo("double"));
            Assert.That(model.Fields["longitude"].Type, Is.EqualTo("double"));
            Assert.That(model.Fields["confirmed"].Type, Is.EqualTo("int"));

            Assert.That(model.Fields["combined_key"].Type, Is.EqualTo("string"));
            Assert.That(model.Fields["combined_key"].Description, Does.Contain("county name+state name+country name"));
        });

        var servers = dataContract.Servers;

        Assert.Multiple(() =>
        {
            Assert.That(servers.Count, Is.EqualTo(1));
            Assert.That(servers.Keys, Does.Contain("s3-json"));

            Assert.That(servers["s3-json"].Type, Is.EqualTo("s3"));
            Assert.That(servers["s3-json"], Is.TypeOf<S3Server>());
            var s3Server = (S3Server)servers["s3-json"];
            Assert.That(s3Server.Location, Is.EqualTo("s3://covid19-lake/enigma-jhu/json/*.json"));
            Assert.That(s3Server.Format, Does.Contain("json"));
            Assert.That(s3Server.Delimiter, Is.EqualTo("new_line"));
        });
    }

    [Test]
    public void Deserialize_OrdersLatestNested_AllFields()
    {
        string resourceName = $"{GetType().Namespace}.Resources.orders-latest-nested.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(dataContract.DataContractSpecification, Is.EqualTo("0.9.3"));
            Assert.That(dataContract.Id, Is.EqualTo("urn:datacontract:checkout:orders-latest-nested"));

            Assert.That(dataContract.Info, Is.Not.Null);
            Assert.That(dataContract.Info!.Title, Is.EqualTo("Orders Latest (Nested)"));
            Assert.That(dataContract.Info.Version, Is.EqualTo("1.0.0"));
            Assert.That(dataContract.Info.Description, Does.StartWith("Successful customer orders in the webshop"));
            Assert.That(dataContract.Info.Owner, Is.EqualTo("Checkout Team"));
            Assert.That(dataContract.Info.Contact, Is.Not.Null);
            Assert.That(dataContract.Info.Contact!.Name, Is.EqualTo("John Doe (Data Product Owner)"));
            Assert.That(dataContract.Info.Contact.Url, Is.EqualTo("https://teams.microsoft.com/l/channel/example/checkout"));

            Assert.That(dataContract.Terms, Is.Not.Null);
            Assert.That(dataContract.Terms!.Usage, Does.Contain("analytics and machine learning"));
            Assert.That(dataContract.Terms.Limitations, Does.Contain("Not suitable for real-time"));
            Assert.That(dataContract.Terms.Billing, Is.EqualTo("5000 USD per month"));
            Assert.That(dataContract.Terms.NoticePeriod, Is.EqualTo("P3M"));
        });

        Assert.Multiple(() =>
        {
            // orders model
            Assert.That(dataContract.Models, Does.ContainKey("orders"));
            var orders = dataContract.Models["orders"];
            Assert.That(orders.Description, Does.Contain("cancelled and deleted orders"));
            Assert.That(orders.Type, Is.EqualTo("table"));

            Assert.That(orders.Fields["order_id"].Required, Is.True);
            Assert.That(orders.Fields["order_id"].Unique, Is.True);
            Assert.That(orders.Fields["order_id"].PrimaryKey, Is.True);

            Assert.That(orders.Fields["order_timestamp"].Type, Is.EqualTo("timestamp"));
            Assert.That(orders.Fields["order_timestamp"].Required, Is.True);

            Assert.That(orders.Fields["order_total"].Type, Is.EqualTo("long"));
            Assert.That(orders.Fields["order_total"].Required, Is.True);

            Assert.That(orders.Fields["customer_id"].Type, Is.EqualTo("text"));
            Assert.That(orders.Fields["customer_id"].MinLength, Is.EqualTo(10));
            Assert.That(orders.Fields["customer_id"].MaxLength, Is.EqualTo(20));

            Assert.That(orders.Fields["customer_email_address"].Format, Is.EqualTo("email"));
            Assert.That(orders.Fields["customer_email_address"].Required, Is.True);

            var address = orders.Fields["address"];
            Assert.That(address.Type, Is.EqualTo("object"));
            Assert.That(address.Fields!["street"].Type, Is.EqualTo("text"));
            Assert.That(address.Fields["city"].Type, Is.EqualTo("text"));
            Assert.That(address.Fields["additional_lines"].Type, Is.EqualTo("array"));
            Assert.That(address.Fields["additional_lines"].Items!.Type, Is.EqualTo("text"));
            Assert.That(address.Fields["additional_lines"].Items!.Description, Is.EqualTo("Additional line"));

            Assert.That(orders.Fields["processed_timestamp"].Required, Is.True);

            // line_items model
            Assert.That(dataContract.Models, Does.ContainKey("line_items"));
            var lineItems = dataContract.Models["line_items"];
            Assert.That(lineItems.Description, Does.Contain("part of an order"));
            Assert.That(lineItems.Type, Is.EqualTo("table"));

            Assert.That(lineItems.Fields["lines_item_id"].Type, Is.EqualTo("text"));
            Assert.That(lineItems.Fields["lines_item_id"].Required, Is.True);
            Assert.That(lineItems.Fields["lines_item_id"].Unique, Is.True);
            Assert.That(lineItems.Fields["lines_item_id"].PrimaryKey, Is.True);

            Assert.That(lineItems.Fields["order_id"].References, Is.EqualTo("orders.order_id"));

            Assert.That(lineItems.Fields["sku"].Description, Does.Contain("purchased article number"));
        });
    }

    [Test]
    public void Deserialize_OrdersLatest_WithTermsAndPolicies()
    {
        string resourceName = $"{GetType().Namespace}.Resources.orders-latest.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);

        Assert.Multiple(() =>
        {
            // Top-level
            Assert.That(dataContract.DataContractSpecification, Is.EqualTo("1.1.0"));
            Assert.That(dataContract.Id, Is.EqualTo("urn:datacontract:checkout:orders-latest"));

            // Info
            Assert.That(dataContract.Info, Is.Not.Null);
            Assert.That(dataContract.Info!.Title, Is.EqualTo("Orders Latest"));
            Assert.That(dataContract.Info.Version, Is.EqualTo("2.0.0"));
            Assert.That(dataContract.Info.Description, Does.Contain("Successful customer orders in the webshop"));
            Assert.That(dataContract.Info.Owner, Is.EqualTo("Checkout Team"));
            Assert.That(dataContract.Info.Contact, Is.Not.Null);
            Assert.That(dataContract.Info.Contact!.Name, Is.EqualTo("John Doe (Data Product Owner)"));
            Assert.That(dataContract.Info.Contact.Url, Is.EqualTo("https://teams.microsoft.com/l/channel/example/checkout"));

            // Terms
            Assert.That(dataContract.Terms, Is.Not.Null);
            Assert.That(dataContract.Terms!.Usage, Does.Contain("analytics and machine learning"));
            Assert.That(dataContract.Terms.Limitations, Does.Contain("Data may not be used to identify individual customers"));
            Assert.That(dataContract.Terms.Billing, Is.EqualTo("5000 USD per month"));
            Assert.That(dataContract.Terms.NoticePeriod, Is.EqualTo("P3M"));

            // Policies
            Assert.That(dataContract.Terms.Policies, Is.Not.Null.And.Count.EqualTo(2));
            Assert.That(dataContract.Terms.Policies[0].Type, Is.EqualTo("privacy-policy").IgnoreCase);
            Assert.That(dataContract.Terms.Policies[0].Url, Is.EqualTo("https://example.com/privacy-policy"));
            Assert.That(dataContract.Terms.Policies[1].Description, Is.EqualTo("External data is licensed under agreement 1234."));
            Assert.That(dataContract.Terms.Policies[1].Url, Is.EqualTo("https://example.com/license/1234"));
        });
    }

    [Test]
    public void Deserialize_Servers_WithServers()
    {
        string resourceName = $"{GetType().Namespace}.Resources.servers.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataContract = GetSerializer().Deserialize(streamReader, Mock.Of<IDataPackageContainer>(), new StorageProvider());

        Assert.That(dataContract, Is.Not.Null);
        var servers = dataContract.Servers;

        Assert.Multiple(() =>
        {
            Assert.That(servers.Count, Is.EqualTo(3));

            Assert.That(servers.Keys, Does.Contain("s3-csv"));
            Assert.That(servers["s3-csv"].Type, Is.EqualTo("s3"));
            Assert.That(servers["s3-csv"], Is.TypeOf<S3Server>());
            var s3Server = (S3Server)servers["s3-csv"];
            Assert.That(s3Server.Location, Is.EqualTo("s3://covid19-lake/enigma-jhu/csv/*.csv"));
            Assert.That(s3Server.Format, Is.EqualTo("csv"));

            Assert.That(servers.Keys, Does.Contain("kafka-orders"));
            Assert.That(servers["kafka-orders"].Type, Is.EqualTo("kafka"));
            Assert.That(servers["kafka-orders"], Is.TypeOf<KafkaServer>());
            var kafkaServer = (KafkaServer)servers["kafka-orders"];
            Assert.That(kafkaServer.Host, Is.EqualTo("pkc-7xoy1.eu-central-1.aws.confluent.cloud:9092"));
            Assert.That(kafkaServer.Topic, Is.EqualTo("orders.v1"));
            Assert.That(kafkaServer.Format, Is.EqualTo("avro"));

            Assert.That(servers.Keys, Does.Contain("pg-customers"));
            Assert.That(servers["pg-customers"].Type, Is.EqualTo("postgresql"));
            Assert.That(servers["pg-customers"], Is.TypeOf<PostgreSqlServer>());
            var pgCustomers = (PostgreSqlServer)servers["pg-customers"];
            Assert.That(pgCustomers.Host, Is.EqualTo("postgres.crm.2"));
            Assert.That(pgCustomers.Database, Is.EqualTo("crm"));
            Assert.That(pgCustomers.Schema, Is.EqualTo("main"));
        });
    }
}
