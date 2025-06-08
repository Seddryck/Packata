using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class CustomServer : BaseServer
{
    /// <summary>
    /// Account used by the server.
    /// </summary>
    [Label("Account")]
    public string? Account { get; set; }

    /// <summary>
    /// Name of the catalog.
    /// </summary>
    [Label("Catalog")]
    public string? Catalog { get; set; }

    /// <summary>
    /// Name of the database.
    /// </summary>
    [Label("Database")]
    public string? Database { get; set; }

    /// <summary>
    /// Name of the dataset.
    /// </summary>
    [Label("Dataset")]
    public string? Dataset { get; set; }

    /// <summary>
    /// Delimiter.
    /// </summary>
    [Label("Delimiter")]
    public string? Delimiter { get; set; }

    /// <summary>
    /// ServerIdentifier endpoint.
    /// </summary>
    [Label("Endpoint URL")]
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    [Label("Format")]
    public string? Format { get; set; }

    /// <summary>
    /// Host name or IP address.
    /// </summary>
    [Label("Host")]
    public string? Host { get; set; }

    /// <summary>
    /// A URL to a location.
    /// </summary>
    [Label("Location")]
    public string? Location { get; set; }

    /// <summary>
    /// Relative or absolute path to the data file(s).
    /// </summary>
    [Label("Path")]
    public string? Path { get; set; }

    /// <summary>
    /// Port to the server. No default value is assumed for custom servers.
    /// </summary>
    [Label("Port")]
    public int? Port { get; set; }

    /// <summary>
    /// Project name.
    /// </summary>
    [Label("Project")]
    public string? Project { get; set; }

    /// <summary>
    /// Cloud region.
    /// </summary>
    [Label("Region")]
    public string? Region { get; set; }

    /// <summary>
    /// Region name.
    /// </summary>
    [Label("Region Name")]
    public string? RegionName { get; set; }

    /// <summary>
    /// The name of the schema.
    /// </summary>
    [Label("Schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    [Label("Service Name")]
    public string? ServiceName { get; set; }

    /// <summary>
    /// Staging directory.
    /// </summary>
    [Label("Staging Directory")]
    public string? StagingDirectory { get; set; }

    /// <summary>
    /// Name of the data stream.
    /// </summary>
    [Label("Stream")]
    public string? stream { get; set; }

    /// <summary>
    /// Name of the cluster or warehouse.
    /// </summary>
    [Label("Warehouse")]
    public string? Warehouse { get; set; }
}
