using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;
public class BaseServer
{
    /// <summary>
    /// Identifier of the server.
    /// </summary>
    /// <value>
    /// Identifier of the server.
    /// </value>
    [Label("Server")]
    public required string Server { get; set; }

    /// <summary>
    /// Type of the server. Can be one of: api, athena, azure, bigquery, clickhouse, databricks,
    /// denodo, dremio, duckdb, glue, cloudsql, db2, informix, kafka, kinesis, local, mysql, oracle,
    /// postgresql, postgres, presto, pubsub, redshift, s3, sftp, snowflake, sqlserver, synapse,
    /// trino, vertica, custom.
    /// </summary>
    /// <value>
    /// Type of the server. Can be one of: api, athena, azure, bigquery, clickhouse, databricks,
    /// denodo, dremio, duckdb, glue, cloudsql, db2, informix, kafka, kinesis, local, mysql, oracle,
    /// postgresql, postgres, presto, pubsub, redshift, s3, sftp, snowflake, sqlserver, synapse,
    /// trino, vertica, custom.
    /// </value>
    [Label("Type")]
    public required string Type { get; set; }

    /// <summary>
    /// Description of the server.
    /// </summary>
    /// <value>
    /// Description of the server.
    /// </value>
    [Label("Description")]
    public string? Description { get; set; }

    /// <summary>
    /// Environment of the server. Examples include: prod, preprod, dev, uat.
    /// </summary>
    /// <value>
    /// Environment of the server. Examples include: prod, preprod, dev, uat.
    /// </value>
    [Label("Environment")]
    public string? Environment { get; set; }

    /// <summary>
    /// List of roles that have access to the server.
    /// </summary>
    /// <value>
    /// List of roles that have access to the server.
    /// </value>
    [Label("Roles")]
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// Custom properties that are not part of the standard.
    /// </summary>
    /// <value>
    /// Custom properties that are not part of the standard.
    /// </value>
    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
