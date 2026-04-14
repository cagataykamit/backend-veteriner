using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    private readonly ITokenHashService _hash;

    public RefreshTokenRepository(AppDbContext db, ITokenHashService hash)
    {
        _db = db;
        _hash = hash;
    }

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
        => _db.RefreshTokens.AddAsync(token, ct).AsTask();

    /// <summary>
    /// Logout gibi senaryolar: yalnızca refresh satırı gerekir (User/Roles yok). İki ek round-trip kaldırılır.
    /// Refresh / select-clinic için <see cref="GetByHashAsync"/> kullanılmaya devam eder.
    /// </summary>
    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = _hash.ComputeSha256(token);
        return _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
    }

    /// <summary>
    /// TokenHash üzerinden tek satır, ardından FK ile User ve UserRoles ayrı SELECT (split-include / geniş join yok).
    /// </summary>
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var rt = await _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

        if (rt is null)
            return null;

        await _db.Entry(rt).Reference(r => r.User).LoadAsync(ct);

        if (rt.User is not null)
            await _db.Entry(rt.User).Collection(u => u.Roles).LoadAsync(ct);

        return rt;
    }

    public Task<List<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null && rt.ExpiresAtUtc > now)
            .OrderByDescending(rt => rt.CreatedAtUtc)
            .ToListAsync(ct);
    }

    // ✅ Yeni: aktif+pasif tüm session’lar
    public Task<List<RefreshToken>> GetByUserAsync(Guid userId, CancellationToken ct = default)
        => _db.RefreshTokens
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

    // ✅ Yeni: tekil session revoke için fetch
    public Task<RefreshToken?> GetByIdAsync(Guid refreshTokenId, CancellationToken ct = default)
        => _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.Id == refreshTokenId, ct);

    public Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Revoke("user_revoked_session");
        return Task.CompletedTask;
    }

    public async Task RevokeAllByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null && rt.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.RevokedAtUtc, now)
                 .SetProperty(r => r.RevokeReason, "logout_all"), ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
