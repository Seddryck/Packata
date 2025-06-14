using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;

/// <summary>
/// Represents a single field in a data model.
/// </summary>
public class Field
{
    /// <summary>
    /// An optional string describing the semantic of the data in this field.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// An optional human-readable title for the field.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The data type of the field.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Indicates if the field must contain a value and may not be null.
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// Nested fields of the object, record, or struct.
    /// </summary>
    public Dictionary<string, Field>? Fields { get; set; }

    /// <summary>
    /// Definition of the item if the field is an array.
    /// </summary>
    public Field? Items { get; set; }

    /// <summary>
    /// Key type for dictionary fields.
    /// </summary>
    public Field? Keys { get; set; }

    /// <summary>
    /// Value type for dictionary fields.
    /// </summary>
    public Field? Values { get; set; }

    /// <summary>
    /// Deprecated: use PrimaryKey instead.
    /// </summary>
    [Obsolete("USe property PrimaryKey")]
    public bool Primary { get => PrimaryKey; set => PrimaryKey = value; }

    /// <summary>
    /// Indicates if this field is a primary key.
    /// </summary>
    public bool PrimaryKey { get; set; } = false;

    /// <summary>
    /// Reference to a field in another model.
    /// </summary>
    public string? References { get; set; }

    /// <summary>
    /// Indicates if the value must be unique within the model.
    /// </summary>
    public bool Unique { get; set; } = false;

    /// <summary>
    /// List of allowed values for this field.
    /// </summary>
    public List<string>? Enum { get; set; }

    /// <summary>
    /// Minimum allowed string length.
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Maximum allowed string length.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Expected format of the field value (e.g., email, uri, uuid).
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Maximum number of digits for numeric values.
    /// </summary>
    public double? Precision { get; set; }

    /// <summary>
    /// Maximum number of decimal places for numeric values.
    /// </summary>
    public double? Scale { get; set; }

    /// <summary>
    /// Regex pattern the field value must match.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Minimum value for numeric types.
    /// </summary>
    public double? Minimum { get; set; }

    /// <summary>
    /// Exclusive minimum value for numeric types.
    /// </summary>
    public double? ExclusiveMinimum { get; set; }

    /// <summary>
    /// Maximum value for numeric types.
    /// </summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Exclusive maximum value for numeric types.
    /// </summary>
    public double? ExclusiveMaximum { get; set; }

    /// <summary>
    /// Deprecated: single example value for the field.
    /// </summary>
    [Obsolete("Use property Examples")]
    public string? Example { get; set; }

    /// <summary>
    /// List of example values for the field.
    /// </summary>
    public List<object>? Examples { get; set; }

    /// <summary>
    /// Indicates if the field contains personally identifiable information.
    /// </summary>
    public bool Pii { get; set; } = false;

    /// <summary>
    /// Data classification label (e.g., sensitive, public).
    /// </summary>
    public string? Classification { get; set; }

    /// <summary>
    /// Custom metadata tags for the field.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Links to external resources.
    /// </summary>
    public Dictionary<string, string>? Links { get; set; }

    /// <summary>
    /// Reference URI to another field definition.
    /// </summary>
    public string? Ref { get; set; }

    /// <summary>
    /// Quality rules that apply to this field.
    /// </summary>
    //public List<QualityRule>? Quality { get; set; }

    /// <summary>
    /// Lineage metadata of the field.
    /// </summary>
    //public LineageInfo? Lineage { get; set; }

    /// <summary>
    /// Platform-specific or tool-specific configuration metadata.
    /// </summary>
    //public FieldConfig? Config { get; set; }
}
