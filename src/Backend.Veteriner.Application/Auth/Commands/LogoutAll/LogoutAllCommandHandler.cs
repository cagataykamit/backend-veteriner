using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.LogoutAll;

public sealed class LogoutAllCommandHandler : IRequestHandler<LogoutAllCommand, Unit>
{
    private readonly IRefreshTokenRepository _refreshRepo;

    public LogoutAllCommandHandler(IRefreshTokenRepository refreshRepo)
    {
        _refreshRepo = refreshRepo;
    }

    public async Task<Unit> Handle(LogoutAllCommand request, CancellationToken ct)
    {
        // 1?? Kullan�c� ID kontrol�
        if (request.UserId == Guid.Empty)
            return Unit.Value;

        // 2?? Kullan�c�n�n t�m aktif tokenlar�n� revoke et
        await _refreshRepo.RevokeAllByUserAsync(request.UserId, ct);

        // 3?? De�i�iklikleri kaydet
        await _refreshRepo.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
