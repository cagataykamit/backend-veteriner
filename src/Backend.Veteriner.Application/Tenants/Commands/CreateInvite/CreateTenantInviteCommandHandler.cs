using System.Security.Cryptography;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.CreateInvite;

public sealed class CreateTenantInviteCommandHandler
    : IRequestHandler<CreateTenantInviteCommand, Result<CreateTenantInviteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly TenantSubscriptionSeatEvaluator _seatEvaluator;
    private readonly IReadRepository<User> _usersRead;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IReadRepository<OperationClaim> _claimsRead;
    private readonly IReadRepository<TenantInvite> _invitesRead;
    private readonly IRepository<TenantInvite> _invitesWrite;
    private readonly ITokenHashService _tokenHash;
    private readonly IUnitOfWork _uow;

    public CreateTenantInviteCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        TenantSubscriptionSeatEvaluator seatEvaluator,
        IReadRepository<User> usersRead,
        IUserTenantRepository userTenantRepo,
        IReadRepository<Clinic> clinicsRead,
        IReadRepository<OperationClaim> claimsRead,
        IReadRepository<TenantInvite> invitesRead,
        IRepository<TenantInvite> invitesWrite,
        ITokenHashService tokenHash,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _seatEvaluator = seatEvaluator;
        _usersRead = usersRead;
        _userTenantRepo = userTenantRepo;
        _clinicsRead = clinicsRead;
        _claimsRead = claimsRead;
        _invitesRead = invitesRead;
        _invitesWrite = invitesWrite;
        _tokenHash = tokenHash;
        _uow = uow;
    }

    public async Task<Result<CreateTenantInviteResultDto>> Handle(CreateTenantInviteCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Auth.PermissionDenied",
                "Davet oluşturmak için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId || jwtTenantId != request.TenantId)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Tenants.AccessDenied",
                "Davet yalnızca oturumdaki kiracı bağlamında oluşturulabilir.");
        }

        var emailNorm = request.Email.Trim().ToLowerInvariant();
        var utcNow = DateTime.UtcNow;

        var seat = await _seatEvaluator.TryBuildAsync(request.TenantId, ct);
        if (!seat.IsSuccess)
            return Result<CreateTenantInviteResultDto>.Failure(seat.Error);

        var snap = seat.Value!;
        if (snap.MemberCount + snap.PendingInviteCount + 1 > snap.MaxUsers)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Subscriptions.UserLimitExceeded",
                "Kiracı kullanıcı kotası dolu; yeni davet oluşturulamaz.");
        }

        var clinic = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByIdSpec(request.TenantId, request.ClinicId), ct);
        if (clinic is null || !clinic.IsActive)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Invites.ClinicInvalid",
                "Klinik bulunamadı, pasif veya bu kiracıya ait değil.");
        }

        var claim = await _claimsRead.FirstOrDefaultAsync(new OperationClaimByIdSpec(request.OperationClaimId), ct);
        if (claim is null)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Invites.OperationClaimNotFound",
                "Geçersiz operationClaimId.");
        }

        if (!InviteAssignableOperationClaimsCatalog.IsAssignableName(claim.Name))
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Invites.OperationClaimNotAssignable",
                "Bu operation claim davet ile atanamaz; yalnızca atanabilir rol listesindekiler seçilebilir.");
        }

        var existingUser = await _usersRead.FirstOrDefaultAsync(new UserByEmailNormalizedSpec(emailNorm), ct);
        if (existingUser is not null)
        {
            if (await _userTenantRepo.ExistsAsync(existingUser.Id, request.TenantId, ct))
            {
                return Result<CreateTenantInviteResultDto>.Failure(
                    "Invites.TargetAlreadyMember",
                    "Bu e-posta zaten bu kiracıda üye.");
            }

            var tid = await _userTenantRepo.GetExistingTenantIdForUserAsync(existingUser.Id, ct);
            if (tid is { } t && t != request.TenantId)
            {
                return Result<CreateTenantInviteResultDto>.Failure(
                    "Invites.TargetUserInAnotherTenant",
                    "Bu e-posta başka bir kiracıya bağlı; davet edilemez.");
            }
        }

        var dup = await _invitesRead.FirstOrDefaultAsync(
            new PendingTenantInviteByTenantEmailSpec(request.TenantId, emailNorm, utcNow), ct);
        if (dup is not null)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Invites.DuplicatePending",
                "Bu e-posta için bekleyen bir davet zaten var.");
        }

        var rawToken = CreateUrlSafeToken();
        var tokenHash = _tokenHash.ComputeSha256(rawToken);

        var expires = request.ExpiresAtUtc is { } ex
            ? (ex.Kind == DateTimeKind.Utc ? ex : ex.ToUniversalTime())
            : utcNow.AddDays(InviteDefaults.DefaultExpiryDays);

        if (expires <= utcNow)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                "Invites.ExpiryInvalid",
                "expiresAtUtc gelecekte bir zaman olmalıdır.");
        }

        var invite = TenantInvite.CreatePending(
            request.TenantId,
            request.ClinicId,
            emailNorm,
            tokenHash,
            request.OperationClaimId,
            expires,
            utcNow);

        await _invitesWrite.AddAsync(invite, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<CreateTenantInviteResultDto>.Success(
            new CreateTenantInviteResultDto(invite.Id, rawToken, emailNorm, invite.TenantId, invite.ClinicId, invite.ExpiresAtUtc));
    }

    private static string CreateUrlSafeToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
