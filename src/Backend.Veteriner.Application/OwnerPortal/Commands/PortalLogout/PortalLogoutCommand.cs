using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.PortalLogout;

/// <summary>
/// Portal çıkışı: <see cref="RefreshToken"/> verilirse yalnız o oturum, <see cref="AllSessions"/> ise hesabın tüm portal
/// oturumları ("tüm cihazlardan çıkış", K3) kapanır. Başka hesabın jetonu veya boş jeton no-op'tur.
/// </summary>
public sealed record PortalLogoutCommand(Guid OwnerAccountId, string? RefreshToken, bool AllSessions)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => AllSessions ? "Portal.Auth.LogoutAll" : "Portal.Auth.Logout";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

public sealed class PortalLogoutCommandHandler : IRequestHandler<PortalLogoutCommand, Result>
{
    private readonly IReadRepository<OwnerPortalRefreshToken> _refreshTokens;
    private readonly OwnerPortalSessionIssuer _sessions;
    private readonly ITokenHashService _hash;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public PortalLogoutCommandHandler(
        IReadRepository<OwnerPortalRefreshToken> refreshTokens,
        OwnerPortalSessionIssuer sessions,
        ITokenHashService hash,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _refreshTokens = refreshTokens;
        _sessions = sessions;
        _hash = hash;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(PortalLogoutCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (request.AllSessions)
        {
            await _sessions.RevokeAllAsync(request.OwnerAccountId, "logout-all", utcNow, ct);
        }
        else if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var stored = await _refreshTokens.FirstOrDefaultAsync(
                new OwnerPortalRefreshTokenByHashSpec(_hash.ComputeSha256(request.RefreshToken.Trim())), ct);
            if (stored is null || stored.OwnerAccountId != request.OwnerAccountId)
                return Result.Success();
            stored.Revoke("logout", utcNow);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
