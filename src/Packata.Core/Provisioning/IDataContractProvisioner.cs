using Packata.Core.Contracts;

namespace Packata.Core.Provisioning;

[Flags]
public enum ContractConstraintOptions
{
    None = 0,
    PrimaryKey = 1,
    Required = 2,
    Unique = 4,
    Checks = 8,
    ForeignKeys = 16,
    All = PrimaryKey | Required | Unique | Checks | ForeignKeys
}

public sealed record ContractProvisioningOptions(
    ContractConstraintOptions Constraints = ContractConstraintOptions.All);

public sealed record ProvisioningDiagnostic(
    string Code,
    string AssetId,
    string Message);

public interface IDataContractProvisioner
{
    IReadOnlyList<ProvisioningDiagnostic> DeploySchema(
        DataContract contract,
        ContractProvisioningOptions? options = null);

    Task<IReadOnlyList<ProvisioningDiagnostic>> LoadDataAsync(
        DataContract contract,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProvisioningDiagnostic>> ExecuteAsync(
        DataContract contract,
        ContractProvisioningOptions? options = null,
        CancellationToken cancellationToken = default);
}
