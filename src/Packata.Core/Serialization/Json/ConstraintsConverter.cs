﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Packata.Core.Serialization.Json;
internal class ConstraintsConverter : JsonConverter
{
    private readonly ConstraintMapper constraintMapper = new();

    public override bool CanConvert(Type objectType)
        => objectType == typeof(List<Constraint>);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var list = new List<Constraint>();

        if (reader.TokenType == JsonToken.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                var propertyName = reader.Value as string ?? throw new NotSupportedException();
                reader.Read();
                if (reader.Value is null)
                    continue;
                var value = reader.Value;
                list.Add(constraintMapper.Map(propertyName, value));
            }
        }

        return list;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
