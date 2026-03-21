using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke;

public sealed class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand, Result>
{
    private readonly IRefreshTokenRepository _repo;

    public RevokeSessionCommandHandler(IRefreshTokenRepository repo)
        => _repo = repo;

    public async Task<Result> Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        var token = await _repo.GetByIdAsync(request.RefreshTokenId, ct)
                    ?? null;

        if (token is null)
        {
            return Result.Failure("Sessions.NotFound", "Oturum bulunamadı.");
        }

        // ✅ kullanıcı sadece kendi token’ını revoke edebilsin
        if (token.UserId != request.UserId)
        {
            return Result.Failure("Sessions.Forbidden", "Bu oturumu kapatma yetkiniz yok.");
        }

        // idempotent davranış: zaten revoked ise sorun etme
        if (token.RevokedAtUtc is null)
        {
            await _repo.RevokeAsync(token, ct);
            await _repo.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
