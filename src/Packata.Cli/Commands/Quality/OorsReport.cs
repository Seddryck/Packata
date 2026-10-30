using Newtonsoft.Json.Linq;
using Packata.Application;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Cli;

internal static class OorsReport
{
    internal const string ApiVersion = QualityReportService.ApiVersion;
    internal const int MaximumOutputCharacters = QualityReportService.MaximumOutputCharacters;
    internal static JObject Create(QualityRunReport report, DataContract contract, string specification, ContractSource source, QualityFilters filters) =>
        QualityReportService.Create(report, contract, specification,
            new(source.Source, source.OriginalText, ApplicationVersion.Value, "packata quality run"), filters);
    internal static void Validate(JObject document) => QualityReportService.Validate(document);
    internal static string Serialize(JObject document, string format) => QualityReportService.Serialize(document, format);
    internal static void Stamp(JObject document, DateTimeOffset start, DateTimeOffset end, TimeSpan elapsed) => QualityReportService.Stamp(document, start, end, elapsed);
    internal static string SafeSource(string source) => QualityReportService.SafeSource(source);
    internal static string Fingerprint(string yaml) => QualityReportService.Fingerprint(yaml);
    internal static JToken BoundedValue(object? value) => QualityReportService.BoundedValue(value);
    internal static string Status(RuleOutcome outcome) => QualityReportService.Status(outcome);
}