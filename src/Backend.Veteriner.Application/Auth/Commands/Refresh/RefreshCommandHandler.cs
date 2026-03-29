using System.Security.Claims;
using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Refresh;

public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, Result<LoginResultDto>>
{
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly ITokenHashService _hash;
    private readonly IJwtTokenService _jwt;
    private readonly IJwtOptionsProvider _opt;
    private readonly IOperationClaimPermissionRepository _ocpRepo;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IUserTenantRepository _userTenants;
    private readonly IReadRepository<Clinic> _clinics;

    public RefreshCommandHandler(
        IRefreshTokenRepository refreshRepo,
        ITokenHashService hash,
        IJwtTokenService jwt,
        IJwtOptionsProvider opt,
        IOperationClaimPermissionRepository ocpRepo,
        IReadRepository<Tenant> tenants,
        IUserTenantRepository userTenants,
        IReadRepository<Clinic> clinics)
    {
        _refreshRepo = refreshRepo;
        _hash = hash;
        _jwt = jwt;
        _opt = opt;
        _ocpRepo = ocpRepo;
        _tenants = tenants;
        _userTenants = userTenants;
        _clinics = clinics;
    }

    public async Task<Result<LoginResultDto>> Handle(RefreshCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.InvalidRefreshToken",
                "Geçersiz refresh token.");
        }

        var tokenHash = _hash.ComputeSha256(request.RefreshToken);
        var stored = await _refreshRepo.GetByHashAsync(tokenHash, ct);

        if (stored is null)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.RefreshTokenNotFound",
                "Refresh token bulunamadı.");
        }

        if (DateTime.UtcNow >= stored.ExpiresAtUtc)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.RefreshTokenExpired",
                "Refresh token süresi dolmuş.");
        }

        if (stored.RevokedAtUtc is not null)
        {
            await _refreshRepo.RevokeAllByUserAsync(stored.UserId, ct);

            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.RefreshTokenReused",
                "Refresh token reuse tespit edildi. Tüm oturumlar sonlandırıldı.");
        }

        var user = stored.User;
        if (user is null)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.UserNotFound",
                "Kullanıcı bulunamadı.");
        }

        if (stored.TenantId is not { } sessionTenantId)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.RefreshSessionRequiresReLogin",
                "Oturum kiracı bilgisi eksik; lütfen yeniden giriş yapın.");
        }

        stored.MarkUsed();

        var permissionCodes = await _ocpRepo.GetPermissionCodesByUserIdAsync(user.Id, ct)
                              ?? Array.Empty<string>();

        var extraClaims = permissionCodes.Select(code => new Claim("permission", code)).ToList();

        if (user.Roles.Any(r => string.Equals(r.Name, "PlatformAdmin", StringComparison.OrdinalIgnoreCase)))
            extraClaims.Add(new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString, ClaimValueTypes.Boolean));

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(sessionTenantId), ct);
        if (tenant is null)
        {
            return Result<LoginResultDto>.Failure(
                "Tenants.NotFound",
                "Kiracı bulunamadı.");
        }

        if (!tenant.IsActive)
        {
            return Result<LoginResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için token yenilenemez.");
        }

        if (!await _userTenants.ExistsAsync(user.Id, sessionTenantId, ct))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.TenantNotMember",
                "Bu kiracıda üyeliğiniz artık yok; yeniden giriş yapın.");
        }

        extraClaims.Add(new Claim(VeterinerClaims.TenantId, sessionTenantId.ToString("D")));

        // Clinic seçildiyse token'a taşınır; geçersizse yeniden seçim zorunlu.
        if (stored.ClinicId is { } sessionClinicId)
        {
            var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(sessionTenantId, sessionClinicId), ct);
            if (clinic is null || !clinic.IsActive)
            {
                return Result<LoginResultDto>.Failure(
                    "Auth.ClinicSelectionRequired",
                    "Aktif klinik bulunamadi; lutfen klinik secimi yapin.");
            }

            extraClaims.Add(new Claim(VeterinerClaims.ClinicId, sessionClinicId.ToString("D")));
        }

        var (access, newRefreshRaw, accessExp) = _jwt.Create(user, extraClaims);

        var newHash = _hash.ComputeSha256(newRefreshRaw);
        stored.ReplaceWith(newHash);

        var newRefresh = new Backend.Veteriner.Domain.Users.RefreshToken(
            user.Id,
            newHash,
            DateTime.UtcNow.AddDays(_opt.RefreshTokenDays),
            null,
            null,
            sessionTenantId,
            stored.ClinicId);

        user.AddRefreshToken(newRefresh);
        await _refreshRepo.AddAsync(newRefresh, ct);
        await _refreshRepo.SaveChangesAsync(ct);

        var dto = new LoginResultDto(access, newRefreshRaw, accessExp, sessionTenantId, null);
        return Result<LoginResultDto>.Success(dto);
    }
}
