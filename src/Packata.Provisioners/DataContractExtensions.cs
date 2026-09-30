using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;

namespace Packata.Provisioners;

public static class DataContractExtensions
{
    public static Task<IReadOnlyList<ProvisioningDiagnostic>> ProvisionAsync(this DataContract contract,
        Func<DubUrlProvisionerBuilder, IDataContractProvisioner> provision,
        ContractProvisioningOptions? options = null, CancellationToken cancellationToken = default) =>
        provision(new()).ExecuteAsync(contract, options, cancellationToken);
}
