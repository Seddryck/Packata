using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;
/// <summary>
/// Describes a logical data model such as a table or view.
/// </summary>
public abstract class BaseServer
{
    /// <summary>
    /// The type of the data product technology that implements the data contract. Well-known server types are: bigquery, s3, glue, redshift, azure, sqlserver, snowflake, databricks, postgres, oracle, kafka, pubsub, sftp, kinesis, trino, local
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// An optional string describing the server.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// An optional string describing the environment, e.g., prod, sit, stg.
    /// </summary>
    public string? Environment { get; set; }
}
