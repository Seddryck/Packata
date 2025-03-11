using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.PathHandling;
using YamlDotNet.Core;

namespace Packata.Core.Serialization.Json;
internal class ResourcesConverter : JsonConverter
{
    private string RootPath { get; }

    public ResourcesConverter(string rootPath)
        => RootPath = rootPath;

    public override bool CanConvert(Type objectType)
        => typeof(List<Resource>).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var resources = JArray.Load(reader);
        var list = new List<Resource>();
        foreach (var resource in resources)
        {
            var obj = new Resource(RootPath);
            serializer.Populate(resource.CreateReader(), obj);
            list.Add(obj);
        }
        return list;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotImplementedException();
}
