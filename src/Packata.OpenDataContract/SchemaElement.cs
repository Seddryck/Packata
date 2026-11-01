using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.OpenDataContract;

public class SchemaElement
{
    /// <summary>
    /// Name of the element.
    /// </summary>
    /// <value>
    /// Name of the element.
    /// </value>
    [Label("Name")]
    public required string Name { get; set; }

    /// <summary>
    /// The physical element data type in the data source.
    /// </summary>
    /// <example>"table", "view", "topic", "file"</example>
    /// <value>
    /// The physical element data type in the data source.
    /// </value>
    [Label("Physical Type")]
    public string? PhysicalType { get; set; }

    /// <summary>
    /// Description of the element.
    /// </summary>
    /// <value>
    /// Description of the element.
    /// </value>
    [Label("Description")]
    public string? Description { get; set; }

    /// <summary>
    /// The business name of the element.
    /// </summary>
    /// <value>
    /// The business name of the element.
    /// </value>
    [Label("Business Name")]
    public string? BusinessName { get; set; }

    /// <summary>
    /// List of links to sources that provide more details on the element; examples include privacy statements, terms and conditions, license agreements, data catalogs, or other tools.
    /// </summary>
    /// <value>
    /// List of links to sources that provide more details on the element; examples include privacy statements, terms and conditions, license agreements, data catalogs, or other tools.
    /// </value>
    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    [Label("Tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Custom properties that are not part of the standard.
    /// </summary>
    /// <value>
    /// Custom properties that are not part of the standard.
    /// </value>
    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];

    /// <summary>Alternative names by which this schema element is known.</summary>
    /// <value>
    /// Alternative names by which this schema element is known.
    /// </value>
    public List<Synonym> Synonyms { get; set; } = [];

    /// <summary>Marks this element as retained for compatibility but no longer recommended.</summary>
    /// <value>
    /// Marks this element as retained for compatibility but no longer recommended.
    /// </value>
    public bool Deprecated { get; set; }
}
