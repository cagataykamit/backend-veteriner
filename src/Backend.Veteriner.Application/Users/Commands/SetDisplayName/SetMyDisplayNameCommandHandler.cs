using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Users.Commands.SetDisplayName;

public sealed class SetMyDisplayNameCommandHandler : IRequestHandler<SetMyDisplayNameCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;

    public SetMyDisplayNameCommandHandler(IUserRepository users, IClientContext client, TimeProvider timeProvider)
    {
        _users = users;
        _client = client;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(SetMyDisplayNameCommand request, CancellationToken ct)
    {
        if (_client.UserId is not { } userId)
        {
            return Result.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı kimliği token içinde bulunamadı.");
        }

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            return Result.Failure("Users.NotFound", "Kullanıcı bulunamadı.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var result = user.SetDisplayName(request.DisplayName, utcNow);
        if (!result.IsSuccess)
            return result;

        await _users.SaveChangesAsync(ct);
        return Result.Success();
    }
}
