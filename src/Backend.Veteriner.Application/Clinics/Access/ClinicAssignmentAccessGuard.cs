using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <inheritdoc cref="IClinicAssignmentAccessGuard"/>
public sealed class ClinicAssignmentAccessGuard : IClinicAssignmentAccessGuard
{
    private readonly IUserOperationClaimRepository _userOperationClaims;

    public ClinicAssignmentAccessGuard(IUserOperationClaimRepository userOperationClaims)
    {
        _userOperationClaims = userOperationClaims;
    }

    public async Task<bool> MustApplyAssignedClinicScopeAsync(Guid userId, CancellationToken ct)
    {
        var claimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        return !TenantWideClaimNames.IsTenantWide(claimNames);
    }
}
