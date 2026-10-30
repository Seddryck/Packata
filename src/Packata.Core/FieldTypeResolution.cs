using System.Data;
using Packata.Core.Contracts;

namespace Packata.Core;

public enum TypeMappingFallback { Reject, LogicalType, Object, String }
public enum TypeMappingTarget { Source, Runtime, Database }
public enum TypeMappingSeverity { Information, Warning, Error }
public enum TemporalSemantics { Local, Offset, Instant }

/// <summary>Fallback is opt-in. Mapping describes representation, not validation of data values.</summary>
public sealed record TypeMappingOptions(TypeMappingFallback Fallback = TypeMappingFallback.Reject,
    string? SourcePath = null, bool PreserveLegacyRepresentation = false)
{
    public static TypeMappingOptions Strict { get; } = new();
    public static TypeMappingOptions Compatible { get; } = new(TypeMappingFallback.LogicalType);
    public static TypeMappingOptions LegacyCompatibility { get; } = new(TypeMappingFallback.LogicalType,
        PreserveLegacyRepresentation: true);
}

public sealed record TypeMappingDiagnostic(string Code, TypeMappingSeverity Severity,
    TypeMappingTarget Target, string FieldPath, string? LogicalType, string? Format,
    string? PhysicalType, string Message);

public sealed record TypeParameters(int? IntegerWidth = null, bool? Signed = null,
    int? Precision = null, int? Scale = null, int? TemporalPrecision = null,
    TemporalSemantics? TemporalSemantics = null, string? Timezone = null);

/// <summary>Target-independent metadata plus independent CLR and DbType projections.</summary>
public sealed record FieldTypeResolution(DataField Field, string FieldPath, string? LogicalType, string? SourceFormat,
    TypeParameters Parameters, Type? RuntimeType, DbType? DatabaseType,
    FieldTypeResolution? Items, IReadOnlyList<FieldTypeResolution> Children,
    IReadOnlyList<TypeMappingDiagnostic> Diagnostics)
{
    public bool Required => Field.Required;
    public bool Nullable => Field.Nullable;

    internal FieldTypeResolution WithNested(FieldTypeResolution? items, FieldTypeResolution[] children)
        => this with { Items = items, Children = Array.AsReadOnly(children),
            Diagnostics = Array.AsReadOnly(Diagnostics.Where(d => d.FieldPath == FieldPath)
                .Concat(items?.Diagnostics ?? []).Concat(children.SelectMany(child => child.Diagnostics)).ToArray()) };

    internal FieldTypeResolution WithRuntime(Type type, string reason)
        => this with { RuntimeType = type, Diagnostics = ProjectionDiagnostics(TypeMappingTarget.Runtime, reason) };

    internal FieldTypeResolution WithDatabase(DbType type, string reason)
        => this with { DatabaseType = type, Diagnostics = ProjectionDiagnostics(TypeMappingTarget.Database, reason) };

    private IReadOnlyList<TypeMappingDiagnostic> ProjectionDiagnostics(TypeMappingTarget target, string reason)
    {
        return Array.AsReadOnly(Diagnostics.Where(d => !(d.Code == "Fallback" && d.Target == target && d.FieldPath == FieldPath))
            .Append(new TypeMappingDiagnostic(reason, TypeMappingSeverity.Information, target, FieldPath,
                Field.LogicalType, Field.Format, Field.PhysicalType,
                "An explicit compatibility/custom representation was selected; source diagnostics still apply.")).ToArray());
    }
}
