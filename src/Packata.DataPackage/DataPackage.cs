using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;

/// <summary>
/// Represents a Data Package as defined by the datapackage.org specification.
/// https://datapackage.org/profiles/2.0/datapackage.json
/// </summary>
public partial class DataPackage
{
    /// <summary>
    /// The profile of this descriptor.
    /// </summary> 
    /// <example>
    /// https://datapackage.org/profiles/2.0/datapackage.json
    /// </example>
    public string Profile { get; set; } = "https://datapackage.org/profiles/1.0/datapackage.json";

    /// <summary>
    /// The name of the data package.
    /// This is ideally a url-usable and human-readable name. Name `SHOULD` be invariant, meaning it `SHOULD NOT` change when its parent descriptor is updated.
    /// Example: "my-dataset".
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// A unique identifier for this package.
    /// Example: "123e4567-e89b-12d3-a456-426614174000".
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// A human-readable title.
    /// Example: "My Package Title"
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// A text description. Markdown is encouraged.
    /// Example: "This is a data package containing data on..."
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The home on the web that is related to this data package.
    /// </summary>
    /// <example>
    /// http://example.com
    /// </example>
    public string? Homepage { get; set; }

    /// <summary>
    /// A unique version number for this descriptor.
    /// Example: "1.0.0" or "1.0.1-beta".
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The datetime on which this descriptor was created.
    /// </summary>
    /// <example>
    /// 1985-04-12T23:20:50.52Z
    /// </example>
    public DateTime Created { get; set; }

    /// <summary>
    /// A list of contributors to the data package.
    /// </summary>
    public List<Contributor> Contributors { get; set; } = [];

    /// <summary>
    /// The keywords associated with the data package.
    /// Example: ["science", "data", "package"].
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>
    /// A image to represent this package.
    /// </summary>
    /// <example>
    /// http://example.com/image.jpg
    /// relative/to/image.jpg
    /// </example>
    public string? Image { get; set; }

    /// <summary>
    /// The license(s) under which this package is published.
    /// Example: "CC-BY-4.0".
    /// </summary>
    public List<License> Licenses { get; set; } = [];

    /// <summary>
    /// A list of resources contained in the data package.
    /// </summary>
    public List<Resource> Resources { get; set; } = [];
}
