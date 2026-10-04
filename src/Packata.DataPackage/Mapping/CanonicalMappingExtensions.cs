using Packata.Core.Contracts;
using Native = Packata.DataPackage.DataPackage;

namespace Packata.DataPackage.Mapping;

public static class CanonicalMappingExtensions
{
    public static MappingResult<DataContract> ToCanonicalContract(this Native source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new DataPackageMapper().Map(source);
    }
}
