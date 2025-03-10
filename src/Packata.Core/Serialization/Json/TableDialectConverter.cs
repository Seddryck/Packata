using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.PathHandling;

namespace Packata.Core.Serialization.Json;
internal class TableDialectConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(TableDialect).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var obj = JObject.Load(reader);

        // Read the "type" property to determine the class to instantiate
        var type = obj["type"]?.ToString() ?? "delimited";
        TableDialect tableDialect = type switch
        {
            "delimited" => new TableDelimitedDialect(),
            "database" => new TableDatabaseDialect(),
            _ => throw new JsonSerializationException($"Unknown type: {type}"),
        };

        // Populate the object properties
        serializer.Populate(obj.CreateReader(), tableDialect);
        return tableDialect;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
