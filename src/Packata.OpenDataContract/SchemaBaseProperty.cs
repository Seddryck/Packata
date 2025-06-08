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
    public bool PrimaryKey { get; set; } = false;

    /// <summary>
    /// If element is a primary key, the position of the primary key element. Starts from 1.
    /// </summary>
    public int? PrimaryKeyPosition { get; set; }

    /// <summary>
    /// The logical element data type.
    /// One of: string, date, number, integer, object, array, boolean.
    /// </summary>
    protected string? LogicalTypeDiscriminator { get; set; }

    /// <summary>
    /// Additional optional metadata to describe the logical type.
    /// </summary>
    protected Dictionary<string, object>? LogicalTypeOptions { get; set; }

    private ILogicalType? _cached;
    public ILogicalType? LogicalType
        => _cached ??= ResolveLogicalType(LogicalTypeDiscriminator, LogicalTypeOptions);

    private ILogicalType ResolveLogicalType(string? discriminator, Dictionary<string, object>? dict)
    {
        if (string.IsNullOrEmpty(discriminator))
            return new UnspecifiedLogicalType();
        return discriminator switch
        {
            "string" => new StringLogicalType(dict),
            "date" => new DateLogicalType(dict),
            "number" => new NumberLogicalType(dict),
            "integer" => new IntegerLogicalType(dict),
            "object" => new ObjectLogicalType(dict),
            "array" => new ArrayLogicalType(dict),
            "boolean" => new BooleanLogicalType(dict),
            _ => new UnknownLogicalType(discriminator)
        };
    }

    /// <summary>
    /// Physical name.
    /// </summary>
    public string? PhysicalName { get; set; }

    /// <summary>
    /// Indicates if the element may contain Null values. Default is false.
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// Indicates if the element contains unique values. Default is false.
    /// </summary>
    public bool? Unique { get; set; }

    /// <summary>
    /// Indicates if the element is partitioned.
    /// </summary>
    public bool? Partitioned { get; set; }

    /// <summary>
    /// If element is used for partitioning, the position of the partition element. Starts from 1. Default is -1.
    /// </summary>
    public int? PartitionKeyPosition { get; set; }

    /// <summary>
    /// Can be anything, like confidential, restricted, and public to more advanced categorization.
    /// </summary>
    public string? Classification { get; set; }

    /// <summary>
    /// The element name within the dataset that contains the encrypted element value.
    /// </summary>
    public string? EncryptedName { get; set; }

    /// <summary>
    /// List of objects in the data source used in the transformation.
    /// </summary>
    public List<string>? TransformSourceObjects { get; set; }

    /// <summary>
    /// Logic used in the element transformation.
    /// </summary>
    public string? TransformLogic { get; set; }

    /// <summary>
    /// Describes the transform logic in very simple terms.
    /// </summary>
    public string? TransformDescription { get; set; }

    /// <summary>
    /// List of sample element values.
    /// </summary>
    public List<object>? Examples { get; set; }

    /// <summary>
    /// True or false indicator; If element is considered a critical data element (CDE) then true else false.
    /// </summary>
    public bool? CriticalDataElement { get; set; }

    /// <summary>
    /// Data quality checks applied to the element.
    /// </summary>
    public object? Quality { get; set; } // Replace with proper type if DataQualityChecks is defined
}
