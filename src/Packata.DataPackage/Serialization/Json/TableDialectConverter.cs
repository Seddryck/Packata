using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Storage;

namespace Packata.DataPackage.Serialization.Json;
internal class TableDialectConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(TableDialect).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);

        var type = obj["type"]?.ToString() ?? InferType(obj);
        TableDialect tableDialect = type switch
        {
            "delimited" => new TableDelimitedDialect(),
            TableStructuredDialect.DialectType => new TableStructuredDialect(),
            "database" => new TableDatabaseDialect(),
            TableSpreadsheetDialect.DialectType => new TableSpreadsheetDialect(),
            _ => throw new JsonSerializationException($"Unknown type: {type}"),
        };

        // Populate the object properties
        serializer.Populate(obj.CreateReader(), tableDialect);
        return tableDialect;
    }

    private static string InferType(JObject obj)
    {
        if (obj.ContainsKey("property") || obj.ContainsKey("itemType") || obj.ContainsKey("itemKeys"))
            return TableStructuredDialect.DialectType;
        if (obj.ContainsKey("sheetNumber") || obj.ContainsKey("sheetName"))
            return TableSpreadsheetDialect.DialectType;
        if (obj.ContainsKey("table"))
            return "database";
        return "delimited";
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
