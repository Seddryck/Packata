namespace Packata.Application.Generation;

public sealed record SchemaCompatibilityProfile(string Id, string Label, string OutputFormat, string Baseline,
    IReadOnlyList<string> Options, IReadOnlyList<string> DiagnosticCapabilities);

/// <summary>Export compatibility is independent of source-generation type mappings.</summary>
public static class SchemaCompatibilityProfiles
{
    public static IReadOnlyList<SchemaCompatibilityProfile> Catalog { get; } = Array.AsReadOnly(new SchemaCompatibilityProfile[] {
        new("hudi", "Hudi", "avro", "Hudi 1.0.2, table version 8; Spark 3.5/Avro 1.12 scalar and nested subset", [],
            ["field-names", "logical-types", "decimal-precision", "managed-columns", "defaults", "constraints"]),
        new("delta", "Delta Lake", "spark-json", "Delta Lake 3.3 / Spark 3.5; reader 1, writer 2, column mapping none; no table features", [],
            ["field-names", "case-insensitive-collisions", "reserved-metadata", "logical-types", "constraints"])
    });

    public static bool Supports(string profile, string format) => Catalog.Any(p => p.Id == profile && p.OutputFormat == format);
}
