using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
/// <summary>
/// Represents a data resource as defined by the Data Resource profile.
/// </summary>
public partial class Resource
{
    /// <summary>
    /// The profile of this descriptor.
    /// </summary>
    /// <example>https://datapackage.org/profiles/2.0/dataresource.json</example>
    public string Profile { get; set; } = "https://datapackage.org/profiles/1.0/dataresource.json";

    /// <summary>
    /// The resource's name.
    /// MUST be unique within the containing Data Package.
    /// </summary>
    /// <remarks>Use lower-case letters with optionally dots, underscores, or dashes.</remarks>
    /// <example>"my_resource"</example>
    public string? Name { get; set; }

    /// <summary>
    /// A human-readable title.
    /// </summary>
    /// <remarks>Suitable for use in user interfaces.</remarks>
    /// <example>"My Resource Title"</example>
    public string? Title { get; set; }

    /// <summary>
    /// A description of the resource.
    /// </summary>
    /// <remarks>Markdown may be used for rich text representation.</remarks>
    /// <example>"This is a description of the resource."</example>
    public string? Description { get; set; }

    /// <summary>
    /// The type of the resource.
    /// </summary>
    /// <remarks>Specifies the nature of the resource, such as "table".</remarks>
    /// <example>"table"</example>
    public string? Type { get; set; }

    /// <summary>
    /// The file format of the resource.
    /// </summary>
    /// <remarks>Should be a file extension such as "csv" or "json".</remarks>
    /// <example>"csv"</example>
    public string? Format { get; set; }

    /// <summary>
    /// The media type of the resource.
    /// </summary>
    /// <remarks>Follows IANA media type definitions such as "text/csv".</remarks>
    /// <example>"text/csv"</example>
    public string? MediaType { get; set; }

    /// <summary>
    /// The character encoding of the resource.
    /// Default: "utf-8".
    /// </summary>
    /// <example>"utf-8"</example>
    public string Encoding { get; set; } = "utf-8";

    /// <summary>
    /// The compression format.
    /// Default: null.
    /// </summary>
    /// <example>"gz", "zz", "zip"</example>
    public string? Compression { get; set; }

    /// <summary>
    /// The size of the resource in bytes.
    /// </summary>
    /// <remarks>Useful for validation and optimization.</remarks>
    /// <example>1024</example>
    public long? Bytes { get; set; }

    /// <summary>
    /// A hash value for the resource.
    /// </summary>
    /// <remarks>Useful for verifying data integrity.</remarks>
    /// <example>"sha256:abcdef1234567890"</example>
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
    public List<IPath> Paths { get; set; } = [];

    /// <summary>
    /// A reference to the data for this resource, as a connection-url
    /// </summary>
    /// <example>
    /// "sqlserver://server001/database001/"
    /// "sqlserver://user:passw0rd@server001/database001/"
    /// </example>
    public IConnection? Connection { get; set; }

    /// <summary>
    /// Inline data for the resource.
    /// </summary>
    /// <remarks>Useful for small datasets.</remarks>
    /// <example>[1, 2, 3]</example>
    public object? Data { get; set; }

    /// <summary>
    /// A schema for the resource.
    /// </summary>
    /// <remarks>May define fields, types, constraints, etc.</remarks>
    /// <example>{ "fields": [{ "name": "id", "type": "integer" }] }</example>
    public Schema? Schema { get; set; }

    /// <summary>
    /// The Table dialect descriptor.
    /// </summary>
    public TableDialect? Dialect { get; set; }

    /// <summary>
    /// A list of sources for this resource.
    /// </summary>
    /// <remarks>Sources can be URLs or other identifiers.</remarks>
    /// <example>["http://example.com/source.csv"]</example>
    public List<Source> Sources { get; set; } = [];
}
