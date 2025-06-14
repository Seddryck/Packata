using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Serialization.Yaml;
using Packata.DataContractSpecification.ServerTypes;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Packata.DataContractSpecification.Serialization.Yaml;
internal class ServerTypeDiscriminator : ITypeDiscriminator
{
    private static Dictionary<string, Type> GetValueMappings()
        => new()
        {
            { "azure", typeof(ServerTypes.AzureServer)},
            { "databricks", typeof(ServerTypes.DatabricksServer)},
            { "kafka", typeof(ServerTypes.KafkaServer)},
            { "local", typeof(ServerTypes.LocalFilesServer)},
            { "oracle", typeof(ServerTypes.OracleServer)},
            { "postgresql", typeof(ServerTypes.PostgreSqlServer)},
            { "s3", typeof(ServerTypes.S3Server)},
            { "sftp", typeof(ServerTypes.SftpServer)},
            { "snowflake", typeof(ServerTypes.SnowflakeServer)},
            { "sqlserver", typeof(ServerTypes.MsSqlServer)},
            { "trino", typeof(ServerTypes.TrinoServer)}
        };

    public void Execute(ITypeDiscriminatingNodeDeserializerOptions options)
        => options.AddKeyValueTypeDiscriminator<BaseServer>("type", GetValueMappings());
}
