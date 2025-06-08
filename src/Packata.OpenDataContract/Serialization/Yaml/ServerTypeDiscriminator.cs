using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Serialization.Yaml;
using Packata.OpenDataContract.ServerTypes;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.OpenDataContract.Serialization.Yaml;
internal class ServerTypeDiscriminator : ITypeDiscriminator
{
    private static Dictionary<string, Type> GetValueMappings()
        => new()
        {
            { "api", typeof(ApiServer)},
            { "azure", typeof(AzureServer)},
            { "clickhouse", typeof(ClickHouseServer)},
            { "databricks", typeof(DatabricksServer)},
            { "db2", typeof(Db2Server)},
            { "denodo", typeof(DenodoServer)},
            { "dremio", typeof(DremioServer)},
            { "duckdb", typeof(DuckDbServer)},
            { "kafka", typeof(KafkaServer)},
            { "local", typeof(LocalFilesServer)},
            { "mysql", typeof(MySqlServer)},
            { "oracle", typeof(OracleServer)},
            { "postgresql", typeof(PostgreSqlServer)},
            { "presto", typeof(PrestorServer)},
            { "s3", typeof(S3Server)},
            { "sftp", typeof(SftpServer)},
            { "snowflake", typeof(SnowflakeServer)},
            { "sqlserver", typeof(MsSqlServer)},
            { "trino", typeof(TrinoServer)},
            { "vertica", typeof(VerticaServer)},
            { "custom", typeof(CustomServer)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<BaseServer>("type", GetValueMappings());
}
