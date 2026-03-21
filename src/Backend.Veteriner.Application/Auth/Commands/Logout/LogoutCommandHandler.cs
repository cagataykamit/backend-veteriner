using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Logout;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IRefreshTokenRepository _refreshRepo;

    public LogoutCommandHandler(IRefreshTokenRepository refreshRepo)
    {
        _refreshRepo = refreshRepo;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        // 1?? Refresh token bo�sa hi�bir �ey yapma
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unit.Value;

        // 2?? Token ver, repo i�inde SHA256 hash�lenip aran�r
        var stored = await _refreshRepo.GetByTokenAsync(request.RefreshToken, ct);
        if (stored is null)
            return Unit.Value; // Token bulunamad�ysa sessiz ge� (bilgi s�zd�rma yok)

        // 3?? Aktif token m�?
        var isActive = stored.RevokedAtUtc is null && stored.ExpiresAtUtc > DateTime.UtcNow;
        if (!isActive)
            return Unit.Value;

        // 4?? Token�� revoke et
        await _refreshRepo.RevokeAsync(stored, ct);

        // 5?? De�i�iklikleri kaydet
        await _refreshRepo.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
