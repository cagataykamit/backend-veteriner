using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeAllMy;

public sealed class RevokeAllMySessionsCommandHandler : IRequestHandler<RevokeAllMySessionsCommand, Result>
{
    private readonly IRefreshTokenRepository _repo;
    private readonly IClientContext _client;

    public RevokeAllMySessionsCommandHandler(IRefreshTokenRepository repo, IClientContext client)
    {
        _repo = repo;
        _client = client;
    }

    public async ValueTask<Result> Handle(RevokeAllMySessionsCommand request, CancellationToken ct)
    {
        var userId = _client.UserId;
        if (userId is null)
            return Result.Failure("Sessions.Unauthorized", "Authenticated user required.");

        if (request.UserId != userId.Value)
            return Result.Failure("Sessions.Forbidden", "Sadece kendi oturumlarınızı kapatabilirsiniz.");

        await _repo.RevokeAllByUserAsync(userId.Value, ct);
        return Result.Success();
    }
}
