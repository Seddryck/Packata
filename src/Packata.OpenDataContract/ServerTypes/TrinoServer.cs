﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class TrinoServer : BaseServer, IHostAware, IDatabaseAware, ISchemaAware
{
    /// <summary>
    /// The host to the database server
    /// </summary>
    [Label("Host")]
    public required string Host { get; set; }

    /// <summary>
    /// The port to the database server. Default is 8080.
    /// </summary>
    [Label("Port")]
    public required int Port { get; set; } = 8080;

    /// <summary>
    /// The name of the database.
    /// </summary>
    [Label("Database")]
    public required string Database { get; set; }

    /// <summary>
    /// The name of the schema.
    /// </summary>
    [Label("Schema")]
    public required string Schema { get; set; }
}
