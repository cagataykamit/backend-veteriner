using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Mediator;

namespace Backend.Veteriner.Application.Public.Queries.InviteDetail;

public sealed class GetPublicTenantInviteDetailQueryHandler
    : IRequestHandler<GetPublicTenantInviteDetailQuery, Result<PublicTenantInviteDetailDto>>
{
    private readonly ITokenHashService _tokenHash;
    private readonly IReadRepository<TenantInvite> _invites;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<User> _usersRead;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly TenantSubscriptionSeatEvaluator _seatEvaluator;
    private readonly IReadRepository<OperationClaim> _operationClaims;
    private readonly TimeProvider _timeProvider;

    public GetPublicTenantInviteDetailQueryHandler(
        ITokenHashService tokenHash,
        IReadRepository<TenantInvite> invites,
        ITenantScope tenantScope,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<User> usersRead,
        IUserTenantRepository userTenantRepo,
        TenantSubscriptionSeatEvaluator seatEvaluator,
        IReadRepository<OperationClaim> operationClaims,
        TimeProvider timeProvider)
    {
        _operationClaims = operationClaims;
        _tokenHash = tokenHash;
        _invites = invites;
        _tenantScope = tenantScope;
        _tenants = tenants;
        _clinics = clinics;
        _usersRead = usersRead;
        _userTenantRepo = userTenantRepo;
        _seatEvaluator = seatEvaluator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PublicTenantInviteDetailDto>> Handle(
        GetPublicTenantInviteDetailQuery request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawToken))
        {
            return Result<PublicTenantInviteDetailDto>.Failure(
                "Invites.TokenRequired",
                "Davet token zorunludur.");
        }

        string hash;
        try
        {
            hash = _tokenHash.ComputeSha256(request.RawToken.Trim());
        }
        catch
        {
            return Result<PublicTenantInviteDetailDto>.Failure(
                "Invites.TokenInvalid",
                "Davet token geçersiz.");
        }

        // Davet bağlantısı anonimdir: token araması kiracılar arasıdır, sonrası davetin kiracısının kapsamında (ADR-040).
        TenantInvite? invite;
        using (_tenantScope.AsSystem())
            invite = await _invites.FirstOrDefaultAsync(new TenantInviteByTokenHashSpec(hash), ct);
        if (invite is null)
        {
            return Result<PublicTenantInviteDetailDto>.Failure(
                "Invites.NotFound",
                "Davet bulunamadı.");
        }

        using var tenantScope = _tenantScope.UseTenant(invite.TenantId);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(invite.TenantId), ct);
        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(invite.TenantId, invite.ClinicId), ct);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var timeExpired = utcNow >= invite.ExpiresAtUtc;
        var isPending = invite.Status == TenantInviteStatus.Pending;
        var isExpired = timeExpired || !isPending;

        var seat = await _seatEvaluator.TryBuildAsync(invite.TenantId, ct);
        var subscriptionOk = seat.IsSuccess;
        var roomForNewMember = false;
        if (seat.IsSuccess)
        {
            var snap = seat.Value!;
            roomForNewMember = snap.MemberCount < snap.MaxUsers;
        }
        var clinicOk = clinic is { IsActive: true };

        var user = await _usersRead.FirstOrDefaultAsync(new UserByEmailNormalizedSpec(invite.Email), ct);
        var requiresSignup = user is null;
        var requiresLogin = user is not null;

        var blockedByUser = false;
        if (user is not null)
        {
            if (await _userTenantRepo.ExistsAsync(user.Id, invite.TenantId, ct))
                blockedByUser = true;
            var otherTid = await _userTenantRepo.GetExistingTenantIdForUserAsync(user.Id, ct);
            if (otherTid is { } ot && ot != invite.TenantId)
                blockedByUser = true;
        }

        var inviteClaim = await _operationClaims.FirstOrDefaultAsync(new OperationClaimByIdSpec(invite.OperationClaimId), ct);

        var actionable = isPending && !timeExpired;
        var canJoin = actionable && subscriptionOk && roomForNewMember && clinicOk && !blockedByUser && tenant is { IsActive: true };

        var dto = new PublicTenantInviteDetailDto(
            request.RawToken.Trim(),
            invite.TenantId,
            tenant?.Name ?? "",
            invite.ClinicId,
            clinic?.Name ?? "",
            invite.Email,
            invite.ExpiresAtUtc,
            isExpired,
            isPending,
            canJoin,
            requiresLogin,
            requiresSignup,
            LegalDocumentCatalog.ToDtos(LegalDocumentCatalog.RequiredFor(inviteClaim is null ? [] : [inviteClaim.Name])));

        return Result<PublicTenantInviteDetailDto>.Success(dto);
    }
}
