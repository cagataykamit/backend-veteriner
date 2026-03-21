using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

public sealed class VerificationTokenRepository : IVerificationTokenRepository
{
    private readonly AppDbContext _db;
    public VerificationTokenRepository(AppDbContext db) => _db = db;

    public Task AddAsync(VerificationToken token, CancellationToken ct = default)
        => _db.VerificationTokens.AddAsync(token, ct).AsTask();

    public Task<VerificationToken?> GetActiveByHashAsync(string tokenHash, VerificationPurpose purpose, CancellationToken ct = default)
        => _db.VerificationTokens
              .Include(v => v.User)
              .FirstOrDefaultAsync(v => v.TokenHash == tokenHash
                                         && v.Purpose == purpose
                                         && v.UsedAtUtc == null
                                         && v.ExpiresAtUtc > DateTime.UtcNow, ct);

    // ? Yeni eklenen metot
    public async Task<VerificationToken?> GetActiveByUserAsync(Guid userId, VerificationPurpose purpose, CancellationToken ct = default)
    {
        return await _db.VerificationTokens
            .Where(x => x.UserId == userId
                     && x.Purpose == purpose
                     && x.UsedAtUtc == null
                     && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
