using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class KafkaServer : BaseServer, IHostAware, IFormatAware, IEncodingAware
{
    /// <summary>
    /// The bootstrap server of the kafka cluster.
    /// </summary>
    /// <value>
    /// The bootstrap server of the kafka cluster.
    /// </value>
    public required string Host { get; set; }

    /// <summary>
    /// The port of the bootstrap server of the kafka cluster. Default is 9092.
    /// </summary>
    /// <value>
    /// The port of the bootstrap server of the kafka cluster. Default is 9092.
    /// </value>
    public object? Port { get; set; } = 9092;

    /// <summary>
    /// File format.
    /// </summary>
    /// <value>
    /// File format.
    /// </value>
    [Label("Format")]
    public required string Format { get; set; }

    public string Encoding { get; set; } = "UTF-8";
}
