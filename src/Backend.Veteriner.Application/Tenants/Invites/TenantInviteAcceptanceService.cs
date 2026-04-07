using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Tenants.Invites;

/// <summary>Davet kabulünde üyelik, klinik ataması ve operation claim bağını tek transaction sınırında tamamlar.</summary>
public sealed class TenantInviteAcceptanceService
{
    private readonly ITenantSubscriptionWriteGuard _writeGuard;
    private readonly TenantSubscriptionSeatEvaluator _seatEvaluator;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IUserClinicRepository _userClinicRepo;
    private readonly IRepository<UserTenant> _userTenantsWrite;
    private readonly IRepository<UserClinic> _userClinicsWrite;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IUnitOfWork _uow;

    public TenantInviteAcceptanceService(
        ITenantSubscriptionWriteGuard writeGuard,
        TenantSubscriptionSeatEvaluator seatEvaluator,
        IUserTenantRepository userTenantRepo,
        IUserClinicRepository userClinicRepo,
        IRepository<UserTenant> userTenantsWrite,
        IRepository<UserClinic> userClinicsWrite,
        IUserOperationClaimRepository userOperationClaims,
        IReadRepository<Clinic> clinics,
        IUnitOfWork uow)
    {
        _writeGuard = writeGuard;
        _seatEvaluator = seatEvaluator;
        _userTenantRepo = userTenantRepo;
        _userClinicRepo = userClinicRepo;
        _userTenantsWrite = userTenantsWrite;
        _userClinicsWrite = userClinicsWrite;
        _userOperationClaims = userOperationClaims;
        _clinics = clinics;
        _uow = uow;
    }

    public async Task<Result<TenantInviteAcceptResultDto>> AcceptAsync(
        TenantInvite invite,
        User user,
        CancellationToken ct)
    {
        var emailNorm = user.Email.Trim().ToLowerInvariant();
        if (emailNorm != invite.Email)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.EmailMismatch",
                "Bu davet farklı bir e-posta adresi içindir; oturum açmış hesabınız eşleşmiyor.");
        }

        if (await _userTenantRepo.ExistsAsync(user.Id, invite.TenantId, ct))
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.AlreadyMember",
                "Bu kiracıda zaten üyesiniz.");
        }

        var existingTenantId = await _userTenantRepo.GetExistingTenantIdForUserAsync(user.Id, ct);
        if (existingTenantId is { } other && other != invite.TenantId)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.UserBelongsToAnotherTenant",
                "Hesabınız başka bir kiracıya bağlı; bu daveti kabul edemezsiniz.");
        }

        var write = await _writeGuard.EnsureWritesAllowedAsync(invite.TenantId, ct);
        if (!write.IsSuccess)
            return Result<TenantInviteAcceptResultDto>.Failure(write.Error);

        var seat = await _seatEvaluator.TryBuildAsync(invite.TenantId, ct);
        if (!seat.IsSuccess)
            return Result<TenantInviteAcceptResultDto>.Failure(seat.Error);

        var snap = seat.Value!;
        if (snap.MemberCount >= snap.MaxUsers)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Subscriptions.UserLimitExceeded",
                "Kiracı kullanıcı kotası doldu; davet kabul edilemez.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(invite.TenantId, invite.ClinicId), ct);
        if (clinic is null || !clinic.IsActive)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.ClinicInvalid",
                "Davetteki klinik bulunamadı, pasif veya bu kiracıya ait değil.");
        }

        await _userTenantsWrite.AddAsync(new UserTenant(user.Id, invite.TenantId), ct);
        if (!await _userClinicRepo.ExistsAsync(user.Id, invite.ClinicId, ct))
            await _userClinicsWrite.AddAsync(new UserClinic(user.Id, invite.ClinicId), ct);

        if (!await _userOperationClaims.ExistsAsync(user.Id, invite.OperationClaimId, ct))
            await _userOperationClaims.AddAsync(new UserOperationClaim(user.Id, invite.OperationClaimId), ct);

        var utcNow = DateTime.UtcNow;
        invite.MarkAccepted(user.Id, utcNow);

        await _uow.SaveChangesAsync(ct);

        return Result<TenantInviteAcceptResultDto>.Success(
            new TenantInviteAcceptResultDto(invite.TenantId, invite.ClinicId, user.Id, "login"));
    }
}
