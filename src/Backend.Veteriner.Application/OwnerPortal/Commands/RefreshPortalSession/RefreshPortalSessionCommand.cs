using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.RefreshPortalSession;

/// <summary>
/// Portal oturumunu yeniler (personel refresh deseniyle paralel, ayrı tablo): jeton döndürülür; iptal edilmiş jetonun tekrar
/// kullanımı hesabın tüm portal oturumlarını kapatır. Personel refresh token'ı burada bulunamaz (ayrı tablo).
/// </summary>
public sealed record RefreshPortalSessionCommand(string RefreshToken)
    : IRequest<Result<PortalAuthTokensDto>>, IIgnoreTenantWriteSubscriptionGuard;

public sealed class RefreshPortalSessionCommandHandler : IRequestHandler<RefreshPortalSessionCommand, Result<PortalAuthTokensDto>>
{
    private readonly IRepository<OwnerPortalRefreshToken> _refreshTokens;
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly OwnerPortalSessionIssuer _sessions;
    private readonly ITokenHashService _hash;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public RefreshPortalSessionCommandHandler(
        IRepository<OwnerPortalRefreshToken> refreshTokens,
        IReadRepository<OwnerAccount> accounts,
        OwnerPortalSessionIssuer sessions,
        ITokenHashService hash,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _refreshTokens = refreshTokens;
        _accounts = accounts;
        _sessions = sessions;
        _hash = hash;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PortalAuthTokensDto>> Handle(RefreshPortalSessionCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidRefreshToken);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var stored = await _refreshTokens.FirstOrDefaultAsync(
            new OwnerPortalRefreshTokenByHashSpec(_hash.ComputeSha256(request.RefreshToken.Trim())), ct);
        if (stored is null)
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidRefreshToken);

        if (stored.IsRevoked)
        {
            await _sessions.RevokeAllAsync(stored.OwnerAccountId, "reuse-detected", utcNow, ct);
            await _uow.SaveChangesAsync(ct);
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.RefreshTokenReused);
        }

        if (stored.IsExpired(utcNow))
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidRefreshToken);

        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(stored.OwnerAccountId), ct);
        if (account is not { IsActive: true })
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidRefreshToken);

        return Result<PortalAuthTokensDto>.Success(await _sessions.IssueAsync(account, utcNow, ct, replacing: stored));
    }
}
