using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.OpenDataContract.ServerTypes;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.OpenDataContract.Serialization.Yaml;
internal interface ITypeDiscriminator
{
    void Execute(ITypeDiscriminatingNodeDeserializerOptions options);
}

internal class ServerTypeDiscriminator : ITypeDiscriminator
{
    private static Dictionary<string, Type> GetValueMappings()
        => new()
        {
            { "api", typeof(ApiServer)},
            { "athena", typeof(AthenaServer)},
            { "azure", typeof(AzureServer)},
            { "btrieve", typeof(ZenServer)},
            { "clickhouse", typeof(ClickHouseServer)},
            { "databricks", typeof(DatabricksServer)},
            { "db2", typeof(Db2Server)},
            { "denodo", typeof(DenodoServer)},
            { "dremio", typeof(DremioServer)},
            { "duckdb", typeof(DuckDbServer)},
            { "exasol", typeof(ExasolServer)},
            { "fastobjects", typeof(PoetServer)},
            { "glue", typeof(GlueServer)},
            { "hana", typeof(HanaServer)},
            { "iceberg", typeof(IcebergServer)},
            { "ingres", typeof(IngresServer)},
            { "kafka", typeof(KafkaServer)},
            { "kinesis", typeof(KinesisServer)},
            { "local", typeof(LocalFilesServer)},
            { "mysql", typeof(MySqlServer)},
            { "oracle", typeof(OracleServer)},
            { "postgresql", typeof(PostgreSqlServer)},
            { "presto", typeof(PrestorServer)},
            { "poet", typeof(PoetServer)},
            { "s3", typeof(S3Server)},
            { "sftp", typeof(SftpServer)},
            { "snowflake", typeof(SnowflakeServer)},
            { "sqlserver", typeof(MsSqlServer)},
            { "teradata", typeof(TeradataServer)},
            { "trino", typeof(TrinoServer)},
            { "vectorwise", typeof(VectorwiseServer)},
            { "versant", typeof(VersantServer)},
            { "vertica", typeof(VerticaServer)},
            { "zen", typeof(ZenServer)},
            { "custom", typeof(CustomServer)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<BaseServer>("type", GetValueMappings());
}
