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
        try { mapping = ArrowSchemaProjection.Map(schema); }
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

    private static string Literal(string value) => GenerationData.Literal(value);
    private static string Metadata(IReadOnlyDictionary<string, string>? values, bool python)
    {
        var pairs = (values ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal);
        return python ? "{" + string.Join(", ", pairs.Select(p => $"{Literal(p.Key)}: {Literal(p.Value)}")) + "}"
            : "new Dictionary<string, string> { " + string.Join(", ", pairs.Select(p => $"[{Literal(p.Key)}] = {Literal(p.Value)}")) + " }";
    }
    private static string Field(Field f, bool python) => python
        ? $"pa.field({Literal(f.Name)}, {Type(f.DataType, true)}, nullable={(f.IsNullable ? "True" : "False")}, metadata={Metadata(f.Metadata, true)})"
        : $"new Field({Literal(f.Name)}, {Type(f.DataType, false)}, {(f.IsNullable ? "true" : "false")}, {Metadata(f.Metadata, false)})";

    private static string Type(IArrowType type, bool python)
    {
        var unit = type switch { TimestampType ts => ts.Unit, Time32Type t32 => t32.Unit, Time64Type t64 => t64.Unit, _ => TimeUnit.Second };
        var pyUnit = unit switch { TimeUnit.Second => "s", TimeUnit.Millisecond => "ms", TimeUnit.Microsecond => "us", _ => "ns" };
        if (type is StructType s) return python ? $"pa.struct([{string.Join(", ", s.Fields.Select(f => Field(f, true)))}])" : $"new StructType(new Field[] {{ {string.Join(", ", s.Fields.Select(f => Field(f, false)))} }})";
        if (type is ListType l) return python ? $"pa.list_({Field(l.ValueField, true)})" : $"new ListType({Field(l.ValueField, false)})";
        if (type is MapType m) return python ? $"pa.map_({Field(m.KeyField, true)}, {Field(m.ValueField, true)})" : $"new MapType({Field(m.KeyField, false)}, {Field(m.ValueField, false)})";
        if (type is TimestampType t) return python ? $"pa.timestamp({Literal(pyUnit)}, tz={(t.Timezone is null ? "None" : Literal(t.Timezone))})" : $"new TimestampType(TimeUnit.{unit}, {(t.Timezone is null ? "(string?)null" : Literal(t.Timezone))})";
        if (type is Time32Type or Time64Type) return python ? $"pa.{(type is Time32Type ? "time32" : "time64")}({Literal(pyUnit)})" : $"new {type.GetType().Name}(TimeUnit.{unit})";
        if (type is Decimal128Type d) return python ? $"pa.decimal128({d.Precision}, {d.Scale})" : $"new Decimal128Type({d.Precision}, {d.Scale})";
        if (type is Decimal256Type d256) return python ? $"pa.decimal256({d256.Precision}, {d256.Scale})" : $"new Decimal256Type({d256.Precision}, {d256.Scale})";
        var function = type.TypeId switch
        {
            ArrowTypeId.Int8 => "int8", ArrowTypeId.UInt8 => "uint8", ArrowTypeId.Int16 => "int16", ArrowTypeId.UInt16 => "uint16",
            ArrowTypeId.Int32 => "int32", ArrowTypeId.UInt32 => "uint32", ArrowTypeId.Int64 => "int64", ArrowTypeId.UInt64 => "uint64",
            ArrowTypeId.Float => "float32", ArrowTypeId.Double => "float64", ArrowTypeId.String => "string", ArrowTypeId.Binary => "binary",
            ArrowTypeId.Boolean => "bool_", ArrowTypeId.Date32 => "date32", _ => throw new NotSupportedException("Arrow code type is unavailable.")
        };
        return python ? $"pa.{function}()" : type.GetType().Name + ".Default";
    }
}
