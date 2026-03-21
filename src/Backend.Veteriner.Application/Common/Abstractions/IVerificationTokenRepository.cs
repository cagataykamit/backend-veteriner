using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IVerificationTokenRepository
{
    Task AddAsync(VerificationToken token, CancellationToken ct = default);
    Task<VerificationToken?> GetActiveByHashAsync(string tokenHash, VerificationPurpose purpose, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<VerificationToken?> GetActiveByUserAsync(Guid userId, VerificationPurpose purpose, CancellationToken ct);

}
