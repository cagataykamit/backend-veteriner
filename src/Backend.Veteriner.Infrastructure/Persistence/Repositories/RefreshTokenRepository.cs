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

    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = _hash.ComputeSha256(token);
        return GetByHashAsync(tokenHash, ct);
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => _db.RefreshTokens
              .Include(rt => rt.User)
              .ThenInclude(u => u.Roles)
              .OrderByDescending(rt => rt.CreatedAtUtc)
              .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

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
