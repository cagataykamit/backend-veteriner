using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeMy;

public sealed class RevokeMySessionCommandHandler : IRequestHandler<RevokeMySessionCommand, Result>
{
    private readonly IRefreshTokenRepository _repo;
    private readonly IClientContext _client;

    public RevokeMySessionCommandHandler(IRefreshTokenRepository repo, IClientContext client)
    {
        _repo = repo;
        _client = client;
    }

    public async Task<Result> Handle(RevokeMySessionCommand request, CancellationToken ct)
    {
        var userId = _client.UserId;
        if (userId is null)
            return Result.Failure("Sessions.Unauthorized", "Authenticated user required.");

        var token = await _repo.GetByIdAsync(request.Id, ct);
        if (token is null)
            return Result.Failure("Sessions.NotFound", "Oturum bulunamadı.");

        if (token.UserId != userId.Value)
            return Result.Failure("Sessions.Forbidden", "Bu oturumu kapatma yetkiniz yok.");

        if (token.RevokedAtUtc is null)
        {
            await _repo.RevokeAsync(token, ct);
            await _repo.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
