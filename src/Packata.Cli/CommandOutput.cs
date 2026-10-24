using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Packata.Core.Contracts;
using System.Text.RegularExpressions;

namespace Packata.Cli;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidDocument = 1;
    public const int Invocation = 2;
    public const int Io = 3;
    public const int Cancelled = 130;
}

internal static class CommandOutput
{
    internal static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() },
        Formatting = Formatting.Indented
    };

    public static void Json(object value) => Console.WriteLine(JsonConvert.SerializeObject(value, JsonSettings));

    // Exception messages from parsers and database drivers may contain source lines or credentials.
    public static MappingDiagnostic Failure(Exception exception) => exception switch
    {
        ContractOperationException operation => operation.Diagnostic,
        OperationCanceledException => new("CLI130", MappingSeverity.Error, "$", "Operation cancelled."),
        IOException or UnauthorizedAccessException or HttpRequestException =>
            new("CLI003", MappingSeverity.Error, "$", "Unable to read the source. Check its location and access permissions."),
        _ => new("CLI001", MappingSeverity.Error, "$", "Invalid or unsupported contract document. Check its syntax and declared format.")
    };

    public static int FailureExitCode(Exception exception) => exception switch
    {
        OperationCanceledException => ExitCodes.Cancelled,
        IOException or UnauthorizedAccessException or HttpRequestException => ExitCodes.Io,
        _ => ExitCodes.InvalidDocument
    };

    public static void Diagnostic(MappingDiagnostic diagnostic) =>
        Console.Error.WriteLine($"{diagnostic.Code} {diagnostic.Severity} {Redact(diagnostic.SourcePath)}: {Redact(diagnostic.Message)}");

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
            foreach (var property in obj.Properties().ToArray())
            {
                if (Regex.IsMatch(property.Name, "(?i)password|pwd|secret|token|credential|connectionUrl|connectionString|api.?key|access.?key|^username$|^uid$", RegexOptions.None, TimeSpan.FromSeconds(1)))
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
}
