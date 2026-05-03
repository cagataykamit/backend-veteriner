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
        var details = await _userOperationClaims.GetDetailsByUserIdAsync(userId, ct);
        var hasTenantAdmin = details.Any(d =>
            string.Equals(d.OperationClaimName, TenantAdminClaimName, StringComparison.OrdinalIgnoreCase));
        if (hasTenantAdmin)
            return false;

        return details.Any(d =>
            string.Equals(d.OperationClaimName, ClinicAdminClaimName, StringComparison.OrdinalIgnoreCase));
    }
}
