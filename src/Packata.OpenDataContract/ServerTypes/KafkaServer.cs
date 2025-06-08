using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class KafkaServer : BaseServer, IHostAware, IFormatAware
{
    /// <summary>
    /// The bootstrap server of the kafka cluster.
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The port of the bootstrap server of the kafka cluster. Default is 9092.
    /// </summary>
    public required int Port { get; set; } = 9092;

    /// <summary>
    /// File format.
    /// </summary>
    [Label("Format")]
    public required string Format { get; set; }
}
