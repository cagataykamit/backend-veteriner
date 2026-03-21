using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Sessions;

public sealed class ListSessionsQueryHandler : IRequestHandler<ListSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    private readonly IRefreshTokenRepository _repo;
    private readonly IClientContext _client;

    public ListSessionsQueryHandler(IRefreshTokenRepository repo, IClientContext client)
    {
        _repo = repo;
        _client = client;
    }

    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(ListSessionsQuery request, CancellationToken ct)
    {
        var userId = _client.UserId;
        if (userId is null)
            return Result<IReadOnlyList<SessionDto>>.Failure("Sessions.Unauthorized", "Authenticated user required.");

        var list = await _repo.GetByUserAsync(userId.Value, ct);

        var dtos = list
            .OrderByDescending(rt => rt.RevokedAtUtc == null ? 1 : 0)
            .ThenByDescending(rt => rt.CreatedAtUtc)
            .Select(rt => new SessionDto(
                rt.Id,
                rt.CreatedAtUtc,
                rt.LastUsedAtUtc,
                rt.ExpiresAtUtc,
                rt.RevokedAtUtc,
                rt.RevokeReason,
                rt.IpAddress,
                rt.UserAgent,
                IsCurrent: false
            ))
            .ToList();

        return Result<IReadOnlyList<SessionDto>>.Success(dtos);
    }
}
