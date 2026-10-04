using Packata.Core.Contracts;
using Native = Packata.OpenDataContract.DataContract;

namespace Packata.OpenDataContract.Mapping;

public static class CanonicalMappingExtensions
{
    public static MappingResult<Packata.Core.Contracts.DataContract> ToCanonicalContract(this Native source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new OpenDataContractMapper().Map(source);
    }
}
