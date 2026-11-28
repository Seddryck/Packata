using Packata.Core.Contracts;
using Native = Packata.OpenDataContract.DataContract;

namespace Packata.OpenDataContract.Mapping;

public static class CanonicalMappingExtensions
{
    /// <summary>Projects the canonical asset's measures for inclusion among an ODCS schema object's properties.</summary>
    public static MappingResult<IReadOnlyList<SchemaProperty>> ToOdcsMeasureProperties(this DataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var diagnostics = new List<MappingDiagnostic>();
        var measures = asset.Schema?.Measures ?? [];
        var properties = measures.Select((measure, index) =>
            MeasureMapper.ToProperty(measure, $"schema/{asset.Name}/measures/{index}", diagnostics)).ToArray();
        return new(properties, diagnostics);
    }

    public static MappingResult<Packata.Core.Contracts.DataContract> ToCanonicalContract(this Native source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new OpenDataContractMapper().Map(source);
    }

    public static MappingResult<Packata.Core.Contracts.DataContract> ToCanonicalContract(this Native source, OdcsMappingOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new OpenDataContractMapper(options).Map(source);
    }
}
