using System.Security.Claims;
using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Tenants.Specs;
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

    public RefreshCommandHandler(
        IRefreshTokenRepository refreshRepo,
        ITokenHashService hash,
        IJwtTokenService jwt,
        IJwtOptionsProvider opt,
        IOperationClaimPermissionRepository ocpRepo,
        IReadRepository<Tenant> tenants)
    {
        _refreshRepo = refreshRepo;
        _hash = hash;
        _jwt = jwt;
        _opt = opt;
        _ocpRepo = ocpRepo;
        _tenants = tenants;
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
        var stored = await _refreshRepo.GetByHashAsync(tokenHash, ct)
                     ?? null;

        if (stored is null)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.RefreshTokenNotFound",
                "Refresh token bulunamadı.");
        }

        // REUSE DETECTION: Aktif olmayan token tekrar kullanılıyorsa tüm oturumları düşür.
        if (!stored.IsActive)
        {
            await _refreshRepo.RevokeAllByUserAsync(stored.UserId, ct);

            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.RefreshTokenReused",
                "Refresh token reuse tespit edildi. Tüm oturumlar sonlandırıldı.");
        }

        // (IsActive zaten expire kontrolü içeriyor; yine de net bırakıyorum)
        if (DateTime.UtcNow >= stored.ExpiresAtUtc)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.RefreshTokenExpired",
                "Refresh token süresi dolmuş.");
        }

        var user = stored.User;
        if (user is null)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Unauthorized.UserNotFound",
                "Kullanıcı bulunamadı.");
        }

        // ✅ Session kullanım izi
        stored.MarkUsed();

        // Kullanıcının güncel permission kodlarını oku
        var permissionCodes = await _ocpRepo.GetPermissionCodesByUserIdAsync(user.Id, ct)
                              ?? Array.Empty<string>();

        // Permission claim'lerini hazırla (tekil 'permission' claim'i)
        var extraClaims = permissionCodes.Select(code => new Claim("permission", code)).ToList();

        if (request.TenantId is { } refreshTenantId)
        {
            if (refreshTenantId == Guid.Empty)
            {
                return Result<LoginResultDto>.Failure(
                    "Validation.TenantId",
                    "TenantId geçersiz.");
            }

            var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(refreshTenantId), ct);
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

            extraClaims.Add(new Claim(VeterinerClaims.TenantId, refreshTenantId.ToString("D")));
        }

        // Yeni access+refresh üret (permission claim’leriyle)
        var (access, newRefreshRaw, accessExp) = _jwt.Create(user, extraClaims);

        // Rotation: eskiyi yeni hash ile işaretle + revoke(reason=rotated)
        var newHash = _hash.ComputeSha256(newRefreshRaw);
        stored.ReplaceWith(newHash);

        // Yeni refresh kaydı (IP/UA şimdilik null)
        var newRefresh = new Backend.Veteriner.Domain.Users.RefreshToken(
            user.Id,
            newHash,
            DateTime.UtcNow.AddDays(_opt.RefreshTokenDays),
            null,
            null);

        user.AddRefreshToken(newRefresh);
        await _refreshRepo.AddAsync(newRefresh, ct);

        // ✅ stored değiştiği için SaveChanges bunu da yazar (tracked ise).
        // RevokeAllByUserAsync gibi methodlar SaveChanges yapmıyorsa burada tek noktadan commit olur.
        await _refreshRepo.SaveChangesAsync(ct);

        var dto = new LoginResultDto(access, newRefreshRaw, accessExp);
        return Result<LoginResultDto>.Success(dto);
    }
}
