using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    Task<List<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);

    // ✅ Yeni: aktif+pasif tüm session’lar (Oturumlarım ekranı)
    Task<List<RefreshToken>> GetByUserAsync(Guid userId, CancellationToken ct = default);

    // ✅ Yeni: tekil session revoke için güvenli fetch (id + user)
    Task<RefreshToken?> GetByIdAsync(Guid refreshTokenId, CancellationToken ct = default);

    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAllByUserAsync(Guid userId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
