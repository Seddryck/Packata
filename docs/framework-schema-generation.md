---
title: Framework schema code
parent: Reference
nav_order: 8
permalink: /reference/dotnet/framework-schema-generation/
---

# Framework schema code

Generate framework definitions through the shared Didot pipeline:

```sh
packata schema generate contract.yaml --asset orders --target pyspark --language python -o orders_schema.py
packata schema generate contract.yaml --asset orders --target arrow --language csharp -o OrdersArrowSchema.cs
packata schema generate contract.yaml --asset orders --target dotnet-dataframe --language csharp -o OrdersDataFrame.cs
packata schema generate contract.yaml --asset orders --target idataview --language csharp -o OrdersDataViewSchema.cs
```

Both selectors are required. Asset selection is optional only for one eligible asset.
Suffixes never select targets/languages. Removed boolean shortcuts and code generation
under `schema export` fail with migration guidance. Existing output files and inputs
are protected against replacement. VS Code exposes **Generate Schema Code > Python >
PySpark Schema…**, and **C# > Arrow Schema… / DataFrame… / ML.NET Schema…**, using the
server catalog, unsaved snapshots and editable untitled previews with explicit save.

## Profiles and names

Custom `--template` selection is separate. Stable `--type-mapping` IDs are
`python-pyspark`, `csharp-arrow`, `csharp-dataframe`, and `csharp-idataview`.
All expose canonical `model.fields` with `required` and `nullable` separately and field
`target.type_expression`. PySpark/Arrow expose `model.target.schema_expression` and field
`target.field_expression`. DataFrame exposes field `target.column_expression`;
IDataView exposes field `target.schema_literal` for canonical metadata.

Use `--type-name` for the schema variable/factory, and `--namespace` for C# only.
Names default to the asset name. C# identifiers are normalized, keywords escaped and
import collisions suffixed deterministically. PySpark variable names use ASCII identifiers
and protect leading digits and imported names/constants. Original field names remain
escaped string literals in source order; duplicate column names fail. Generation reads
metadata only: no data loading, compilation, cursor implementation or Spark job execution.

## Complete examples and prerequisites

Given `orders` with required non-null `id: integer/i64`, nullable `name: string`, and
required non-null `amount: number` with precision 12, scale 2, the following complete
examples show usage. Actual generated output also includes Packata metadata.

### PySpark

Python 3.10+, PySpark 3.5+ (interop tested with 3.5.7); importing needs no SparkSession.

```python
from pyspark.sql.types import StructType, StructField, LongType, StringType, DecimalType

Orders = StructType([
    StructField("id", LongType(), nullable=False),
    StructField("name", StringType(), nullable=True),
    StructField("amount", DecimalType(12, 2), nullable=False),
])

# Elsewhere: from orders_schema import Orders
# With an existing SparkSession: spark.createDataFrame([], schema=Orders)
assert Orders["amount"].dataType == DecimalType(12, 2)
```

Native StructType/StructField/ArrayType/MapType constructors use the same validated
projection as `schema export --output-format spark-json`, including nested nullability
and the Packata metadata envelope. Supported types: signed integer widths, widened
unsigned integers (u64 fails), boolean, string, fp32/fp64, date, decimal up to 38 digits,
instant timestamps, structs, arrays and non-null string-key maps. Opaque objects,
time-only values, local timestamps and temporal precision above microseconds fail.
Original per-value offsets/zones cannot be retained; diagnostics explain the loss.
Requiredness and constraints remain metadata and need separate validation.

### Apache Arrow C#

Install `Apache.Arrow` **23.0.0**. See [Arrow generation]({{ '/reference/dotnet/schema-generation/' | relative_url }}) for nested,
decimal and temporal mappings and metadata policies.

```csharp
using Apache.Arrow;
using Apache.Arrow.Types;

public static class OrdersArrowSchema
{
    public static Schema Create() => new Schema(new Field[] {
        new Field("id", Int64Type.Default, false),
        new Field("name", StringType.Default, true),
        new Field("amount", new Decimal128Type(12, 2), false)
    }, null);
}
// Application code: Schema schema = OrdersArrowSchema.Create();
```

### Microsoft.Data.Analysis DataFrame

Install `Microsoft.Data.Analysis` **0.21.1**. Supported columns: string, boolean, all
signed/unsigned integer widths, fp32/fp64 and decimal with explicit precision/scale up to
28. Temporal and nested/logical shapes fail with field-specific conversion guidance.
Columns permit nulls; presence, non-nullability and constraints need separate validation.

```csharp
using Microsoft.Data.Analysis;

public static class OrdersDataFrame
{
    public static DataFrame Create() => new DataFrame(new DataFrameColumn[] {
        new PrimitiveDataFrameColumn<long>("id", 0),
        new StringDataFrameColumn("name", 0),
        new PrimitiveDataFrameColumn<decimal>("amount", 0)
    });
}
// Application code: DataFrame frame = OrdersDataFrame.Create();
// frame.Rows.Count == 0; frame.Columns[0].DataType == typeof(long)
```

### ML.NET DataViewSchema

Install `Microsoft.ML.DataView` **3.0.1**, also supplied by compatible Microsoft.ML
packages. Supported scalars: boolean, text, signed/unsigned integer widths, fp32/fp64.
Decimal has no lossless ML.NET mapping: **the decimal input above fails** with a field
diagnostic. If the intended source contract declares `amount` as fp64, it generates:

```csharp
using Microsoft.ML;
using Microsoft.ML.Data;

public static class OrdersDataViewSchema
{
    public static DataViewSchema Create()
    {
        var builder = new DataViewSchema.Builder();
        builder.AddColumn("id", NumberDataViewType.Int64);
        builder.AddColumn("name", TextDataViewType.Instance);
        builder.AddColumn("amount", NumberDataViewType.Double);
        return builder.ToSchema();
    }
}
// Application code: DataViewSchema schema = OrdersDataViewSchema.Create();
// schema["amount"].Type == NumberDataViewType.Double
```

Generated columns also carry canonical field JSON in text annotation `packata.schema`,
preserving requiredness/nullability, constraints and categories separately. ML.NET has no
independent null bitmap: NaN, empty text and key zero have type-specific missing-value
semantics; integer/boolean scalars cannot distinguish null. Nested objects/maps, nested
arrays, temporal types and decimal fail rather than being coerced.

Arrays of scalar items become variable vectors by default. ODCS field custom properties
can declare dimensions or explicit key cardinality:

```yaml
- name: features
  logicalType: array
  customProperties:
    - property: mlnet
      value:
        dimensions: [2, 3]
  items:
    name: value
    logicalType: number
    logicalTypeOptions:
      format: fp32
- name: category
  logicalType: integer
  logicalTypeOptions:
    format: u32
  customProperties:
    - property: mlnet
      value:
        keyCount: 7
```

Dimensions must be nonnegative Int32 sizes with an Int32-sized product; zero means unknown
length. `keyCount` is positive and within unsigned byte/u16/u32/u64 storage capacity.
Keys are one-based categorical indices; zero means missing. Domains never imply keys.
For vector keys, put `keyCount` on the item field. Canonical adapters can supply the same
object in `Extensions["mlnet"]`. Unknown/malformed options fail. No category encoding,
slot-name inference or KeyValues annotation is invented. Data Package uses variable
vectors and ordinary scalars unless an adapter supplies the canonical namespace.

## Validation

`FrameworkGenerationTests` compiles generated C#, checks column names/types/order and
zero rows, vector dimensions, keys and annotations, and compares equivalent ODCS/Data
Package definitions. Set `PACKATA_TEST_PYSPARK_PYTHON` to Python with PySpark for native
StructType/Spark JSON equivalence. `ArrowGenerationTests` supports PyArrow via
`PACKATA_TEST_PYTHON`. CLI, LanguageServer and extension routing tests cover selectors,
diagnostics, previews and protected output.
