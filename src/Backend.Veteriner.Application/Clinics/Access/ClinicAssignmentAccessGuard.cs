using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <inheritdoc cref="IClinicAssignmentAccessGuard"/>
public sealed class ClinicAssignmentAccessGuard : IClinicAssignmentAccessGuard
{
    private const string TenantAdminClaimName = "Admin";
    private const string ClinicAdminClaimName = "ClinicAdmin";

    private readonly IUserOperationClaimRepository _userOperationClaims;

    public ClinicAssignmentAccessGuard(IUserOperationClaimRepository userOperationClaims)
    {
        _userOperationClaims = userOperationClaims;
    }

    public async Task<bool> MustApplyAssignedClinicScopeAsync(Guid userId, CancellationToken ct)
    {
        var claimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        var hasTenantAdmin = claimNames.Any(name =>
            string.Equals(name, TenantAdminClaimName, StringComparison.OrdinalIgnoreCase));
        if (hasTenantAdmin)
            return false;

        return claimNames.Any(name =>
            string.Equals(name, ClinicAdminClaimName, StringComparison.OrdinalIgnoreCase));
    }
}
