using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Common;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Me;

public sealed class GetAccountSummaryQueryHandler
    : IRequestHandler<GetAccountSummaryQuery, Result<AccountSummaryDto>>
{
    private readonly IClientContext _client;
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IUserReadRepository _users;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IUserOperationClaimRepository _userOperationClaims;

    public GetAccountSummaryQueryHandler(
        IClientContext client,
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IUserReadRepository users,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IUserOperationClaimRepository userOperationClaims)
    {
        _client = client;
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _users = users;
        _tenants = tenants;
        _clinics = clinics;
        _userOperationClaims = userOperationClaims;
    }

    public async Task<Result<AccountSummaryDto>> Handle(GetAccountSummaryQuery request, CancellationToken ct)
    {
        var userId = _client.UserId;
        if (userId is null)
        {
            return Result<AccountSummaryDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var user = await _users.FirstOrDefaultAsync(new UserByIdWithRolesSpec(userId.Value), ct);
        if (user is null)
        {
            return Result<AccountSummaryDto>.Failure(
                "Users.NotFound",
                "Kullanıcı bulunamadı.");
        }

        Guid? tenantId = _tenantContext.TenantId;
        string? tenantName = null;
        if (tenantId is { } tid)
        {
            var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tid), ct);
            tenantName = tenant?.Name;
        }

        Guid? activeClinicId = _clinicContext.ClinicId;
        string? activeClinicName = null;
        if (activeClinicId is { } cid && tenantId is { } clinicTenantId)
        {
            var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(clinicTenantId, cid), ct);
            activeClinicName = clinic?.Name;
        }

        var claimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId.Value, ct);
        var roles = claimNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var displayName = TenantMemberDisplayName.DeriveFromEmail(user.Email);

        return Result<AccountSummaryDto>.Success(new AccountSummaryDto(
            user.Id,
            user.Email,
            FirstName: null,
            LastName: null,
            displayName,
            tenantId,
            tenantName,
            activeClinicId,
            activeClinicName,
            roles,
            TenantWideClaimNames.IsTenantWide(claimNames)));
    }
}
