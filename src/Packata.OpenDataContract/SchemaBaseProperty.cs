using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.OpenDataContract.Types;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract;
public abstract class SchemaBaseProperty : SchemaElement
{
    /// <summary>
    /// Boolean value specifying whether the element is primary or not. Default is false.
    /// </summary>
    /// <value>
    /// Boolean value specifying whether the element is primary or not. Default is false.
    /// </value>
    public bool PrimaryKey { get; set; } = false;

    /// <summary>
    /// If element is a primary key, the position of the primary key element. Starts from 1.
    /// </summary>
    /// <value>
    /// If element is a primary key, the position of the primary key element. Starts from 1.
    /// </value>
    public int? PrimaryKeyPosition { get; set; }

    /// <summary>
    /// The logical element data type.
    /// One of: string, date, number, integer, object, array, boolean.
    /// </summary>
    /// <value>
    /// The logical element data type.
    /// One of: string, date, number, integer, object, array, boolean.
    /// </value>
    protected string? LogicalTypeDiscriminator { get; set; }

    /// <summary>
    /// Additional optional metadata to describe the logical type.
    /// </summary>
    /// <value>
    /// Additional optional metadata to describe the logical type.
    /// </value>
    protected Dictionary<string, object>? LogicalTypeOptions { get; set; }

    internal IReadOnlyDictionary<string, object>? DeclaredLogicalTypeOptions => LogicalTypeOptions;

    private ILogicalType? _cached;
    public ILogicalType? LogicalType
        => _cached ??= ResolveLogicalType(LogicalTypeDiscriminator, LogicalTypeOptions);

    private ILogicalType ResolveLogicalType(string? discriminator, Dictionary<string, object>? dict)
    {
        if (string.IsNullOrEmpty(discriminator))
            return new UnspecifiedLogicalType();
        if (discriminator is "string" or "boolean" or "integer" or "array" or "object"
            && dict is not null && (dict.ContainsKey("precision") || dict.ContainsKey("scale")))
            throw new YamlDotNet.Core.YamlException("Precision and scale require a decimal logical type.");
        return discriminator switch
        {
            "string" => new StringLogicalType(dict),
            "date" => new DateLogicalType(dict),
            "timestamp" => new TimestampLogicalType(dict),
            "datetime" => new TimestampLogicalType(dict),
            "time" => new TimeLogicalType(dict),
            "number" => new NumberLogicalType(dict),
            "decimal" => new NumberLogicalType(dict),
            "integer" => new IntegerLogicalType(dict),
            "object" => new ObjectLogicalType(dict),
            "array" => new ArrayLogicalType(dict),
            "map" => new MapLogicalType(dict),
            "vector" => new VectorLogicalType(dict),
            "boolean" => new BooleanLogicalType(dict),
            _ => new UnknownLogicalType(discriminator)
        };
    }

    /// <summary>
    /// Physical name.
    /// </summary>
    /// <value>
    /// Physical name.
    /// </value>
    public string? PhysicalName { get; set; }

    /// <summary>
    /// Indicates if the element may contain Null values. Default is false.
    /// </summary>
    /// <value>
    /// Indicates if the element may contain Null values. Default is false.
    /// </value>
    public bool? Required { get; set; }

    /// <summary>Whether a present value may be null, independently of requiredness.</summary>
    /// <value>
    /// Whether a present value may be null, independently of requiredness.
    /// </value>
    public bool? Nullable { get; set; }

    /// <summary>
    /// Indicates if the element contains unique values. Default is false.
    /// </summary>
    /// <value>
    /// Indicates if the element contains unique values. Default is false.
    /// </value>
    public bool? Unique { get; set; }

    /// <summary>
    /// Indicates if the element is partitioned.
    /// </summary>
    /// <value>
    /// Indicates if the element is partitioned.
    /// </value>
    public bool? Partitioned { get; set; }

    /// <summary>
    /// If element is used for partitioning, the position of the partition element. Starts from 1. Default is -1.
    /// </summary>
    /// <value>
    /// If element is used for partitioning, the position of the partition element. Starts from 1. Default is -1.
    /// </value>
    public int? PartitionKeyPosition { get; set; }

    /// <summary>
    /// Can be anything, like confidential, restricted, and public to more advanced categorization.
    /// </summary>
    /// <value>
    /// Can be anything, like confidential, restricted, and public to more advanced categorization.
    /// </value>
    public string? Classification { get; set; }

    /// <summary>
    /// The element name within the dataset that contains the encrypted element value.
    /// </summary>
    /// <value>
    /// The element name within the dataset that contains the encrypted element value.
    /// </value>
    public string? EncryptedName { get; set; }

    /// <summary>
    /// List of objects in the data source used in the transformation.
    /// </summary>
    /// <value>
    /// List of objects in the data source used in the transformation.
    /// </value>
    public List<string>? TransformSourceObjects { get; set; }

    /// <summary>
    /// Logic used in the element transformation.
    /// </summary>
    /// <value>
    /// Logic used in the element transformation.
    /// </value>
    public string? TransformLogic { get; set; }

    /// <summary>
    /// Describes the transform logic in very simple terms.
    /// </summary>
    /// <value>
    /// Describes the transform logic in very simple terms.
    /// </value>
    public string? TransformDescription { get; set; }

    /// <summary>
    /// List of sample element values.
    /// </summary>
    /// <value>
    /// List of sample element values.
    /// </value>
    public List<object>? Examples { get; set; }

    /// <summary>
    /// True or false indicator; If element is considered a critical data element (CDE) then true else false.
    /// </summary>
    /// <value>
    /// True or false indicator; If element is considered a critical data element (CDE) then true else false.
    /// </value>
    public bool? CriticalDataElement { get; set; }

    /// <summary>
    /// Data quality checks applied to the element.
    /// </summary>
    /// <value>
    /// Data quality checks applied to the element.
    /// </value>
    public List<DataQualityRule> Quality { get; set; } = [];

    /// <summary>
    /// Relationships originating at this property. The source is implicit when omitted.
    /// </summary>
    /// <value>
    /// Relationships originating at this property. The source is implicit when omitted.
    /// </value>
    public List<Relationship> Relationships { get; set; } = [];

    /// <summary>Semantic role of the property: column, measure, or dimension.</summary>
    /// <value>
    /// Semantic role of the property: column, measure, or dimension.
    /// </value>
    public string? SemanticType { get; set; }

    /// <summary>Permitted values and their descriptive metadata.</summary>
    /// <value>
    /// Permitted values and their descriptive metadata.
    /// </value>
    public List<EnumerationValue>? Enum { get; set; }

    /// <summary>Key and value definitions when <c>logicalType</c> is map.</summary>
    /// <value>
    /// Key and value definitions when <c>logicalType</c> is map.
    /// </value>
    public MapDefinition? Map { get; set; }

    /// <summary>Element definition when <c>logicalType</c> is array.</summary>
    /// <value>
    /// Element definition when <c>logicalType</c> is array.
    /// </value>
    public SchemaProperty? Items { get; set; }

    /// <summary>Nested fields when <c>logicalType</c> is object.</summary>
    /// <value>
    /// Nested fields when <c>logicalType</c> is object.
    /// </value>
    public List<SchemaProperty> Properties { get; set; } = [];
}
