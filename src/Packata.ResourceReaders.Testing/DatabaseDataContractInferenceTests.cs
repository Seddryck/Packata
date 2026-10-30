using System.Data;
using System.Data.Common;
using DubUrl.Discovery;
using DubUrl.Provider.DuckDb.Discovery;
using DubUrl.Provider.Sqlite.Discovery;
using DubUrl.Schema;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.ResourceReaders.Database;

namespace Packata.ResourceReaders.Testing;

public class DatabaseDataContractInferenceTests
{
    [Test]
    public void Map_preserves_integer_domains_and_view_decimal_parameters()
    {
        var nativeTypes = new[] { DbType.SByte, DbType.Byte, DbType.Int16, DbType.UInt16,
            DbType.Int32, DbType.UInt32, DbType.Int64, DbType.UInt64 };
        var formats = new[] { "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64" };
        var columns = nativeTypes.Select((type, index) => new MetadataColumn("value" + index, index, type, false)).ToArray();
        var coverage = Enum.GetValues<SchemaFeature>().ToDictionary(x => x, _ => MetadataCoverage.Available);
        var metadata = new SchemaMetadata([new MetadataTable(new("numbers"), columns)], coverage, [])
        {
            Views = [new MetadataView(new("amounts"),
                [new MetadataColumn("amount", 0, DbType.Decimal, true) { Precision = 38, Scale = 10 }], null, [])]
        };
        var inspection = new SchemaInspector().Map(metadata, Basic);
        var contract = new DatabaseDataContractInference(new() { DiscoveryOptions = Basic })
            .Map(inspection, new ConnectionLocation("mssql"), "database").RequireContract();
        var fields = contract.Assets.Single(a => a.Name == "numbers").Schema!.Fields;
        for (var index = 0; index < nativeTypes.Length; index++)
        {
            Assert.That(fields[index].Format, Is.EqualTo(formats[index]));
            Assert.That(Core.FieldTypeResolver.Resolve(fields[index], Core.TypeMappingOptions.Strict).DatabaseType,
                Is.EqualTo(nativeTypes[index]));
        }
        var amount = Core.FieldTypeResolver.Resolve(contract.Assets.Single(a => a.Name == "amounts").Schema!.Fields[0],
            Core.TypeMappingOptions.Strict);
        Assert.That(amount.Parameters.Precision, Is.EqualTo(38));
        Assert.That(amount.Parameters.Scale, Is.EqualTo(10));
        Assert.That(amount.RuntimeType, Is.Null);
        Assert.That(amount.DatabaseType, Is.EqualTo(DbType.Decimal));
        Assert.That(amount.Nullable, Is.True);
        Assert.That(amount.Required, Is.False);
        Assert.That(fields.All(field => !field.Nullable), Is.True);
    }

    private static readonly DiscoveryOptions Basic = new()
    {
        Features =
        [
            SchemaFeature.Tables, SchemaFeature.Columns, SchemaFeature.PrimaryKeys,
            SchemaFeature.ForeignKeys, SchemaFeature.Indexes, SchemaFeature.Views
        ],
        IncludeReferencedTables = true,
        AllowPartial = true
    };

    [Test]
    public async Task InferAsync_inspects_sqlite_tables_relationships_views_and_redacts_credentials()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE customer (id INTEGER NOT NULL PRIMARY KEY, code TEXT NOT NULL);
            CREATE UNIQUE INDEX customer_code ON customer(code);
            CREATE TABLE purchase (id INTEGER NOT NULL PRIMARY KEY, customer_id INTEGER NOT NULL,
                amount DECIMAL(10,2), FOREIGN KEY(customer_id) REFERENCES customer(id));
            CREATE VIEW customer_codes AS SELECT id, code FROM customer;
            """);
        var options = new DatabaseDataContractInferenceOptions { DiscoveryOptions = Basic };
        var location = new ConnectionLocation("sqlite", Database: ":memory:",
            ConnectionUrl: "sqlite://reader:top-secret@/:memory:?mode=memory&sslPassword=hidden" +
                "&access_token=also-secret#local-note");

        var result = await new DatabaseDataContractInference(options)
            .InferAsync(connection, new SqliteMetadataReader(), location);

        var contract = result.RequireContract();
        var customer = contract.Assets.Single(asset => asset.Name == "customer");
        var purchase = contract.Assets.Single(asset => asset.Name == "purchase");
        var customerSchema = customer.Schema!;
        var code = customerSchema.Fields.Single(field => field.Name == "code");
        var endpoint = contract.Endpoints.Single(item => item.Id == customer.EndpointBindings.Single().EndpointId);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(customer.Kind, Is.EqualTo(AssetKind.Table));
            Assert.That(customerSchema.PrimaryKey, Is.EqualTo(new[] { "id" }));
            Assert.That(code.Constraints.Single(constraint => constraint.Kind == "unique").Value,
                Is.EqualTo(true));
            Assert.That(purchase.Schema!.Relationships.Single().TargetAsset, Is.EqualTo(customer.Id));
            Assert.That(contract.Assets.Single(asset => asset.Name == "customer_codes").Kind,
                Is.EqualTo(AssetKind.View));
            Assert.That(endpoint.Location, Is.TypeOf<ConnectionLocation>());
            Assert.That(((ConnectionLocation)endpoint.Location).ConnectionUrl,
                Is.EqualTo("sqlite:///:memory:?mode=memory&sslPassword=%5BREDACTED%5D" +
                    "&access_token=%5BREDACTED%5D"));
            Assert.That(endpoint.ToString(), Does.Not.Contain("top-secret").And.Not.Contain("also-secret")
                .And.Not.Contain("hidden").And.Not.Contain("local-note"));
        });
    }

    [Test]
    public async Task InferAsync_inspects_duckdb_column_facets_and_composite_keys()
    {
        await using var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE account (tenant_id INTEGER NOT NULL, account_id BIGINT NOT NULL,
                balance DECIMAL(12,3), CONSTRAINT account_pk PRIMARY KEY (tenant_id, account_id));
            """);
        var options = new DatabaseDataContractInferenceOptions
        {
            DiscoveryOptions = Basic with { Features = Basic.Features.Where(x => x != SchemaFeature.Views).ToArray() }
        };

        var result = await new DatabaseDataContractInference(options).InferAsync(
            connection, new DuckDbMetadataReader(), new ConnectionLocation("duckdb", Database: ":memory:"));

        var asset = result.RequireContract().Assets.Single();
        var balance = asset.Schema!.Fields.Single(field => field.Name == "balance");
        Assert.Multiple(() =>
        {
            Assert.That(asset.Schema.PrimaryKey, Is.EqualTo(new[] { "tenant_id", "account_id" }));
            Assert.That(asset.Schema.Fields.Single(field => field.Name == "account_id").Format,
                Is.EqualTo("i64"));
            Assert.That(balance.LogicalType, Is.EqualTo("number"));
            Assert.That(balance.Constraints.Single(x => x.Kind == "precision").Value, Is.EqualTo(12));
            Assert.That(balance.Constraints.Single(x => x.Kind == "scale").Value, Is.EqualTo(3));
        });
    }

    [Test]
    public void Map_preserves_provider_neutral_metadata_and_applies_overrides()
    {
        var customer = new MetadataTable(new MetadataName("customer", "sales"),
        [
            new MetadataColumn("id", 0, DbType.Int32, false),
            new MetadataColumn("name", 1, DbType.String, true) { Length = 80, Comment = "Display name" }
        ]) { PrimaryKey = new MetadataKey("customer_pk", ["id"]), Comment = "Customers" };
        var order = new MetadataTable(new MetadataName("order", "sales"),
        [
            new MetadataColumn("id", 0, DbType.Int64, false),
            new MetadataColumn("customer_id", 1, DbType.Int32, false),
            new MetadataColumn("code", 2, DbType.String, false),
            new MetadataColumn("amount", 3, DbType.Decimal, false) { Precision = 10, Scale = 2 }
        ])
        {
            PrimaryKey = new MetadataKey("order_pk", ["id"]),
            ForeignKeys = [new MetadataForeignKey("order_customer", ["customer_id"], customer.Identity, ["id"])],
            Indexes = [new MetadataIndex(new MetadataName("order_code", "sales"), [new("code")], true)],
            Checks = [new MetadataCheck("amount_positive", new("column", "amount"), ">=", new("value", Value: 0m))]
        };
        var coverage = Enum.GetValues<SchemaFeature>().ToDictionary(x => x, _ => MetadataCoverage.Available);
        var metadata = new SchemaMetadata([customer, order], coverage, []);
        var inspection = new SchemaInspector().Map(metadata, new DiscoveryOptions { AllowPartial = true });
        var options = new DatabaseDataContractInferenceOptions
        {
            ContractIdentity = new ContractIdentity("commerce", "Commerce", "1.0"),
            ContractMetadata = new ContractMetadata("Commerce database", "Operational store"),
            Assets = new Dictionary<string, DatabaseAssetInferenceOptions>
            {
                ["sales.order"] = new("orders", "Orders", "Placed orders")
            }
        };

        var result = new DatabaseDataContractInference(options).Map(inspection,
            new ConnectionLocation("mssql", "db.example", 1433, "commerce"), "mssql://db.example/commerce");

        var contract = result.RequireContract();
        var orders = contract.Assets.Single(asset => asset.Id == "orders");
        var amount = orders.Schema!.Fields.Single(field => field.Name == "amount");
        var code = orders.Schema.Fields.Single(field => field.Name == "code");
        Assert.Multiple(() =>
        {
            Assert.That(contract.Identity, Is.EqualTo(options.ContractIdentity));
            Assert.That(contract.Metadata, Is.EqualTo(options.ContractMetadata));
            Assert.That(orders.Name, Is.EqualTo("Orders"));
            Assert.That(orders.Description, Is.EqualTo("Placed orders"));
            Assert.That(orders.Schema.Relationships.Single().TargetAsset,
                Is.EqualTo(contract.Assets.Single(asset => asset.Name == "customer").Id));
            Assert.That(amount.Constraints.Single(x => x.Kind == "minimum").Value, Is.EqualTo(0m));
            Assert.That(Core.FieldTypeResolver.Resolve(amount, Core.TypeMappingOptions.Strict).Parameters.Precision, Is.EqualTo(10));
            Assert.That(Core.FieldTypeResolver.Resolve(amount, Core.TypeMappingOptions.Strict).Parameters.Scale, Is.EqualTo(2));
            Assert.That(code.Constraints.Single(x => x.Kind == "unique").Value, Is.EqualTo(true));
            Assert.That(((ConnectionLocation)contract.Endpoints.Single().Location).Namespace, Is.EqualTo("sales"));
        });
    }

    [Test]
    public void Map_returns_error_diagnostics_when_discovery_produces_no_schema()
    {
        var inspection = new SchemaInspectionResult(null, false, false,
            new Dictionary<SchemaFeature, MetadataCoverage>(),
            [new DiscoveryDiagnostic("SDI001", SchemaFeature.Tables, MetadataCoverage.Unsupported,
                "The provider does not support schema discovery.")]);

        var result = new DatabaseDataContractInference().Map(inspection,
            new ConnectionLocation("unknown"), "unknown://./");

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.Null);
            Assert.That(result.IsSuccessful, Is.False);
            Assert.That(result.Diagnostics.Single().Severity, Is.EqualTo(InferenceSeverity.Error));
            Assert.That(() => result.RequireContract(), Throws.TypeOf<InvalidOperationException>());
        });
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
