using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using System.Text.RegularExpressions;

namespace Packata.Documents;

public static class ContractJson
{
    public static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() },
        Formatting = Formatting.Indented
    };

    public static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?i)([a-z][a-z0-9+.-]*://)[^/\s@]+@", "$1[redacted]@", RegexOptions.None, TimeSpan.FromSeconds(1));
        return Regex.Replace(value,
            @"(?i)\b([\w.-]*(?:password|pwd|token|secret|api[_-]?key|access[_-]?key|credential|username|user|uid)[\w.-]*)\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^;\s&,}]+)",
            "$1=[redacted]", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    public static JToken RedactedJson(object value)
    {
        var token = JToken.FromObject(value, JsonSerializer.Create(JsonSettings));
        Scrub(token);
        return token;
    }

    private static void Scrub(JToken token)
    {
        if (token is JObject obj)
        {
            // ODCS custom properties encode the property name as data rather than a JSON key.
            if (new[] { "property", "name" }.Any(name => obj[name]?.Type == JTokenType.String
                && IsSensitiveProperty((string)obj[name]!)) && obj["value"] is not null)
                obj["value"] = "[redacted]";
            foreach (var property in obj.Properties().ToArray())
            {
                if (IsSensitiveProperty(property.Name))
                    property.Value = "[redacted]";
                else Scrub(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array) Scrub(child);
        }
        else if (token is JValue { Type: JTokenType.String } text)
            text.Value = Redact((string?)text.Value ?? string.Empty);
    }

    public static bool IsSensitiveProperty(string name) => Regex.IsMatch(name,
        "(?i)password|pwd|secret|token|credential|connectionUrl|connectionString|api.?key|access.?key|^username$|^uid$", RegexOptions.None, TimeSpan.FromSeconds(1));
}
