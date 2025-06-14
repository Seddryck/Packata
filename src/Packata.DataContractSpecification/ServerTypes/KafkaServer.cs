using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.DataContractSpecification.ServerTypes;
public class KafkaServer : BaseServer
{
    /// <summary>
    /// The bootstrap server of the kafka cluster.
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The topic name.
    /// </summary>
    public required string Topic { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    [Label("Format")]
    public required string Format { get; set; } = "json";
}
