using Apache.Arrow;
using Apache.Arrow.Types;
using Packata.Arrow;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

internal static class ArrowCodeProjection
{
    internal static void Apply(Dictionary<string, object?> context, DataSchema schema, bool python, List<GenerationDiagnostic> diagnostics)
    {
        ArrowSchemaResult mapping;
        try { mapping = ArrowSchemaMapper.Map(schema); }
        catch (NotSupportedException e) { diagnostics.Add(new("ARROW_METADATA", "Error", "$", e.Message)); return; }
        diagnostics.AddRange(mapping.Diagnostics.Select(d => new GenerationDiagnostic(d.Code, d.IsError ? "Error" : "Warning", d.Path, d.Message)));
        if (mapping.Schema is not { } arrow) return;
        var target = (Dictionary<string, object?>)context["target"]!;
        var name = ((string)target["name"]!).TrimStart('@');
        if (python && name is "pa" or "schema" or "False" or "True" or "None") name += "Schema";
        target["name"] = name;
        ((Dictionary<string, object?>)context["asset"]!)["generated_name"] = name;
        target["schema_expression"] = python
            ? $"pa.schema([{string.Join(", ", arrow.FieldsList.Select(f => Field(f, true)))}], metadata={Metadata(arrow.Metadata, true)})"
            : $"new Schema(new Field[] {{ {string.Join(", ", arrow.FieldsList.Select(f => Field(f, false)))} }}, {Metadata(arrow.Metadata, false)})";
        ApplyFields((Dictionary<string, object?>[])context["fields"]!, arrow.FieldsList, python);
    }

    private static void ApplyFields(Dictionary<string, object?>[] contexts, IReadOnlyList<Field> fields, bool python)
    {
        for (var i = 0; i < contexts.Length; i++)
        {
            var target = (Dictionary<string, object?>)contexts[i]["target"]!;
            target["type_expression"] = Type(fields[i].DataType, python);
            target["field_expression"] = Field(fields[i], python);
            if (fields[i].DataType is StructType s) ApplyFields((Dictionary<string, object?>[])contexts[i]["children"]!, s.Fields, python);
            if (fields[i].DataType is ListType l && contexts[i]["items"] is Dictionary<string, object?> item) ApplyFields([item], [l.ValueField], python);
        }
    }

    private static string Literal(string value, bool python = false) => python
        ? Newtonsoft.Json.JsonConvert.ToString(ContractJson.Redact(value), '"', Newtonsoft.Json.StringEscapeHandling.Default)
        : GenerationData.Literal(value);
    private static string Metadata(IReadOnlyDictionary<string, string>? values, bool python)
    {
        var pairs = (values ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal);
        return python ? "{" + string.Join(", ", pairs.Select(p => $"{Literal(p.Key, true)}: {Literal(p.Value, true)}")) + "}"
            : "new Dictionary<string, string> { " + string.Join(", ", pairs.Select(p => $"[{Literal(p.Key)}] = {Literal(p.Value)}")) + " }";
    }
    private static string Field(Field f, bool python) => python
        ? $"pa.field({Literal(f.Name, true)}, {Type(f.DataType, true)}, nullable={(f.IsNullable ? "True" : "False")}, metadata={Metadata(f.Metadata, true)})"
        : $"new Field({Literal(f.Name)}, {Type(f.DataType, false)}, {(f.IsNullable ? "true" : "false")}, {Metadata(f.Metadata, false)})";

    private static string Type(IArrowType type, bool python) => type switch
    {
        StructType structure => Struct(structure, python),
        ListType list => List(list, python),
        MapType map => Map(map, python),
        TimestampType timestamp => Timestamp(timestamp, python),
        Time32Type time => Time(time.Unit, "Time32Type", "time32", python),
        Time64Type time => Time(time.Unit, "Time64Type", "time64", python),
        Decimal128Type number => Decimal(number.Precision, number.Scale, 128, python),
        Decimal256Type number => Decimal(number.Precision, number.Scale, 256, python),
        _ => Scalar(type, python)
    };

    private static string Struct(StructType type, bool python) => python
        ? $"pa.struct([{string.Join(", ", type.Fields.Select(f => Field(f, true)))}])"
        : $"new StructType(new Field[] {{ {string.Join(", ", type.Fields.Select(f => Field(f, false)))} }})";

    private static string List(ListType type, bool python) => python
        ? $"pa.list_({Field(type.ValueField, true)})" : $"new ListType({Field(type.ValueField, false)})";

    private static string Map(MapType type, bool python) => python
        ? $"pa.map_({Field(type.KeyField, true)}, {Field(type.ValueField, true)})"
        : $"new MapType({Field(type.KeyField, false)}, {Field(type.ValueField, false)})";

    private static string Timestamp(TimestampType type, bool python)
    {
        var zone = type.Timezone is null ? "None" : Literal(type.Timezone, true);
        if (python) return $"pa.timestamp({Literal(PythonUnit(type.Unit), true)}, tz={zone})";
        zone = type.Timezone is null ? "(string?)null" : Literal(type.Timezone);
        return $"new TimestampType(TimeUnit.{type.Unit}, {zone})";
    }

    private static string PythonUnit(TimeUnit unit) => unit switch
    {
        TimeUnit.Second => "s", TimeUnit.Millisecond => "ms", TimeUnit.Microsecond => "us", _ => "ns"
    };

    private static string Time(TimeUnit unit, string csharpType, string pythonType, bool python) => python
        ? $"pa.{pythonType}({Literal(PythonUnit(unit), true)})" : $"new {csharpType}(TimeUnit.{unit})";

    private static string Decimal(int precision, int scale, int width, bool python) => python
        ? $"pa.decimal{width}({precision}, {scale})" : $"new Decimal{width}Type({precision}, {scale})";

    private static readonly IReadOnlyDictionary<ArrowTypeId, string> ScalarFunctions = new Dictionary<ArrowTypeId, string>
    {
        [ArrowTypeId.Int8] = "int8", [ArrowTypeId.UInt8] = "uint8", [ArrowTypeId.Int16] = "int16", [ArrowTypeId.UInt16] = "uint16",
        [ArrowTypeId.Int32] = "int32", [ArrowTypeId.UInt32] = "uint32", [ArrowTypeId.Int64] = "int64", [ArrowTypeId.UInt64] = "uint64",
        [ArrowTypeId.Float] = "float32", [ArrowTypeId.Double] = "float64", [ArrowTypeId.String] = "string", [ArrowTypeId.Binary] = "binary",
        [ArrowTypeId.Boolean] = "bool_", [ArrowTypeId.Date32] = "date32"
    };

    private static string Scalar(IArrowType type, bool python)
    {
        if (!ScalarFunctions.TryGetValue(type.TypeId, out var function)) throw new NotSupportedException("Arrow code type is unavailable.");
        return python ? $"pa.{function}()" : type.GetType().Name + ".Default";
    }
}
