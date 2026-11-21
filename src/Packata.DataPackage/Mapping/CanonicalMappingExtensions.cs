using Packata.Core.Contracts;
using Native = Packata.DataPackage.DataPackage;

namespace Packata.DataPackage.Mapping;

public static class CanonicalMappingExtensions
{
    public static MappingResult<DataContract> ToCanonicalContract(this Native source)
        => source.ToCanonicalContract(new DataPackageMappingOptions());

    public static MappingResult<DataContract> ToCanonicalContract(this Native source, DataPackageMappingOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new DataPackageMapper(options).Map(source);
    }
}
