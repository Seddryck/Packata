using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.DataPackage;
/// <summary>
/// Represents a data resource as defined by the Data Resource profile.
/// </summary>
public partial class Resource
{
    /// <summary>
    /// The profile of this descriptor.
    /// </summary>
    /// <example>https://datapackage.org/profiles/2.0/dataresource.json</example>
    /// <value>
    /// The profile of this descriptor.
    /// </value>
    public string Profile { get; set; } = "https://datapackage.org/profiles/1.0/dataresource.json";

    /// <summary>
    /// The resource's name.
    /// MUST be unique within the containing Data Package.
    /// </summary>
    /// <remarks>Use lower-case letters with optionally dots, underscores, or dashes.</remarks>
    /// <example>"my_resource"</example>
    /// <value>
    /// The resource's name.
    /// MUST be unique within the containing Data Package.
    /// </value>
    public string? Name { get; set; }

    /// <summary>
    /// A human-readable title.
    /// </summary>
    /// <remarks>Suitable for use in user interfaces.</remarks>
    /// <example>"My Resource Title"</example>
    /// <value>
    /// A human-readable title.
    /// </value>
    public string? Title { get; set; }

    /// <summary>
    /// A description of the resource.
    /// </summary>
    /// <remarks>Markdown may be used for rich text representation.</remarks>
    /// <example>"This is a description of the resource."</example>
    /// <value>
    /// A description of the resource.
    /// </value>
    public string? Description { get; set; }

    /// <summary>The home on the web related to this resource.</summary>
    /// <value>
    /// The home on the web related to this resource.
    /// </value>
    public string? Homepage { get; set; }

    /// <summary>
    /// The type of the resource.
    /// </summary>
    /// <remarks>Specifies the nature of the resource, such as "table".</remarks>
    /// <example>"table"</example>
    /// <value>
    /// The type of the resource.
    /// </value>
    public string? Type { get; set; }

    /// <summary>
    /// The type of the resource.
    /// </summary>
    /// <remarks>Specifies the nature of the resource, such as "table".</remarks>
    /// <example>"virtual", "local", "service", "remote"</example>
    /// <value>
    /// The type of the resource.
    /// </value>
    public string? Kind { get; set; }

    /// <summary>
    /// The file format of the resource.
    /// </summary>
    /// <remarks>Should be a file extension such as "csv" or "json".</remarks>
    /// <example>"csv"</example>
    /// <value>
    /// The file format of the resource.
    /// </value>
    public string? Format { get; set; }

    /// <summary>
    /// The media type of the resource.
    /// </summary>
    /// <remarks>Follows IANA media type definitions such as "text/csv".</remarks>
    /// <example>"text/csv"</example>
    /// <value>
    /// The media type of the resource.
    /// </value>
    public string? MediaType { get; set; }

    /// <summary>
    /// The character encoding of the resource.
    /// Default: "utf-8".
    /// </summary>
    /// <example>"utf-8"</example>
    /// <value>
    /// The character encoding of the resource.
    /// Default: "utf-8".
    /// </value>
    public string Encoding { get; set; } = "utf-8";

    /// <summary>
    /// The compression format.
    /// Default: null.
    /// </summary>
    /// <example>"gz", "zz", "zip"</example>
    /// <value>
    /// The compression format.
    /// Default: null.
    /// </value>
    public string? Compression { get; set; }

    /// <summary>
    /// The size of the resource in bytes.
    /// </summary>
    /// <remarks>Useful for validation and optimization.</remarks>
    /// <example>1024</example>
    /// <value>
    /// The size of the resource in bytes.
    /// </value>
    public long? Bytes { get; set; }

    /// <summary>
    /// A hash value for the resource.
    /// </summary>
    /// <remarks>Useful for verifying data integrity.</remarks>
    /// <example>"sha256:abcdef1234567890"</example>
    /// <value>
    /// A hash value for the resource.
    /// </value>
    public string? Hash { get; set; }

    /// <summary>
    /// A reference to the data for this resource, as either a path as a string, or an array of paths as strings. of valid URIs.
    /// </summary>
    /// <remarks>A fully qualified URL, or a POSIX file path.</remarks>
    /// <example>
    /// "/path/to/resource.csv"
    /// "http://example.com/file.csv"
    /// [ "file_1.csv", "file_2.csv" ]
    /// [ "http://example.com/file_1.csv", "http://example.com/file_2.csv" ]
    /// </example>
    /// <value>
    /// A reference to the data for this resource, as either a path as a string, or an array of paths as strings. of valid URIs.
    /// </value>
    public List<IPath> Paths { get; set; } = [];

    /// <summary>
    /// A reference to the data for this resource, as a connection-url
    /// </summary>
    /// <example>
    /// "sqlserver://server001/database001/"
    /// "sqlserver://user:passw0rd@server001/database001/"
    /// </example>
    /// <value>
    /// A reference to the data for this resource, as a connection-url
    /// </value>
    public IConnection? Connection { get; set; }

    /// <summary>
    /// Inline data for the resource.
    /// </summary>
    /// <remarks>
    /// Useful for small datasets. Deserialized arrays of records are exposed as
    /// <c>IEnumerable&lt;IDictionary&lt;string, object?&gt;&gt;</c>, with native CLR values.
    /// Other inline data shapes retain their scalar, dictionary, or array structure.
    /// </remarks>
    /// <example>[{ "id": 1, "name": "Alice" }]</example>
    /// <value>
    /// Inline data for the resource.
    /// </value>
    public object? Data { get; set; }

    /// <summary>
    /// A schema for the resource.
    /// </summary>
    /// <remarks>May define fields, types, constraints, etc.</remarks>
    /// <example>{ "fields": [{ "name": "id", "type": "integer" }] }</example>
    /// <value>
    /// A schema for the resource.
    /// </value>
    public Schema? Schema { get; set; }

    /// <summary>Value, label, and ordering metadata from the Category Tables recipe.</summary>
    /// <value>
    /// Value, label, and ordering metadata from the Category Tables recipe.
    /// </value>
    public CategoryFieldMap? CategoryFieldMap { get; set; }

    /// <summary>
    /// The Table dialect descriptor.
    /// </summary>
    /// <value>
    /// The Table dialect descriptor.
    /// </value>
    public TableDialect? Dialect { get; set; }

    /// <summary>
    /// A list of sources for this resource.
    /// </summary>
    /// <remarks>Sources can be URLs or other identifiers.</remarks>
    /// <example>["http://example.com/source.csv"]</example>
    /// <value>
    /// A list of sources for this resource.
    /// </value>
    public List<Source> Sources { get; set; } = [];

    /// <summary>Resource-specific licenses; inherited from the package when empty.</summary>
    /// <value>
    /// Resource-specific licenses; inherited from the package when empty.
    /// </value>
    public List<License> Licenses { get; set; } = [];
}
