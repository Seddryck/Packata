using Packata.DataPackage.Mapping;
using Packata.OpenDataContract.Mapping;

namespace Packata.Application;

public sealed record ContractMappingOptions(OdcsMappingOptions Odcs, DataPackageMappingOptions DataPackage)
{
    public static ContractMappingOptions Default { get; } = new(new(), new());
}
