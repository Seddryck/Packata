using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract.ServerTypes;
public class CustomServer : BaseServer, IEncodingAware
{
    /// <summary>
    /// Account used by the server.
    /// </summary>
    /// <value>
    /// Account used by the server.
    /// </value>
    [Label("Account")]
    public string? Account { get; set; }

    /// <summary>
    /// Name of the catalog.
    /// </summary>
    /// <value>
    /// Name of the catalog.
    /// </value>
    [Label("Catalog")]
    public string? Catalog { get; set; }

    /// <summary>
    /// Name of the database.
    /// </summary>
    /// <value>
    /// Name of the database.
    /// </value>
    [Label("Database")]
    public string? Database { get; set; }

    /// <summary>
    /// Name of the dataset.
    /// </summary>
    /// <value>
    /// Name of the dataset.
    /// </value>
    [Label("Dataset")]
    public string? Dataset { get; set; }

    /// <summary>
    /// Delimiter.
    /// </summary>
    /// <value>
    /// Delimiter.
    /// </value>
    [Label("Delimiter")]
    public string? Delimiter { get; set; }

    /// <summary>
    /// ServerIdentifier endpoint.
    /// </summary>
    /// <value>
    /// ServerIdentifier endpoint.
    /// </value>
    [Label("Endpoint URL")]
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// File format.
    /// </summary>
    /// <value>
    /// File format.
    /// </value>
    [Label("Format")]
    public string? Format { get; set; }

    /// <summary>
    /// Host name or IP address.
    /// </summary>
    /// <value>
    /// Host name or IP address.
    /// </value>
    [Label("Host")]
    public string? Host { get; set; }

    /// <summary>
    /// A URL to a location.
    /// </summary>
    /// <value>
    /// A URL to a location.
    /// </value>
    [Label("Location")]
    public string? Location { get; set; }

    /// <summary>
    /// Relative or absolute path to the data file(s).
    /// </summary>
    /// <value>
    /// Relative or absolute path to the data file(s).
    /// </value>
    [Label("Path")]
    public string? Path { get; set; }

    /// <summary>
    /// Port to the server. No default value is assumed for custom servers.
    /// </summary>
    /// <value>
    /// Port to the server. No default value is assumed for custom servers.
    /// </value>
    [Label("Port")]
    public object? Port { get; set; }

    public string Encoding { get; set; } = "UTF-8";

    /// <summary>
    /// Project name.
    /// </summary>
    /// <value>
    /// Project name.
    /// </value>
    [Label("Project")]
    public string? Project { get; set; }

    /// <summary>
    /// Cloud region.
    /// </summary>
    /// <value>
    /// Cloud region.
    /// </value>
    [Label("Region")]
    public string? Region { get; set; }

    /// <summary>
    /// Region name.
    /// </summary>
    /// <value>
    /// Region name.
    /// </value>
    [Label("Region Name")]
    public string? RegionName { get; set; }

    /// <summary>
    /// The name of the schema.
    /// </summary>
    /// <value>
    /// The name of the schema.
    /// </value>
    [Label("Schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    /// <value>
    /// Name of the service.
    /// </value>
    [Label("Service Name")]
    public string? ServiceName { get; set; }

    /// <summary>
    /// Staging directory.
    /// </summary>
    /// <value>
    /// Staging directory.
    /// </value>
    [Label("Staging Directory")]
    public string? StagingDirectory { get; set; }

    /// <summary>
    /// Name of the data stream.
    /// </summary>
    /// <value>
    /// Name of the data stream.
    /// </value>
    [Label("Stream")]
    public string? stream { get; set; }

    /// <summary>
    /// Name of the cluster or warehouse.
    /// </summary>
    /// <value>
    /// Name of the cluster or warehouse.
    /// </value>
    [Label("Warehouse")]
    public string? Warehouse { get; set; }
}
