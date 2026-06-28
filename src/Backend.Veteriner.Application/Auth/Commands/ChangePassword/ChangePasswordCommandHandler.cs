using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IClientContext _client;

    public ChangePasswordCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IClientContext client)
    {
        _users = users;
        _hasher = hasher;
        _client = client;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var userId = _client.UserId;
        if (userId is null)
            return Result.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı kimliği token içinde bulunamadı.");

        var user = await _users.GetByIdAsync(userId.Value, ct);
        if (user is null)
            return Result.Failure("Auth.ChangePassword.UserNotFound", "Kullanıcı bulunamadı.");

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Failure("Auth.ChangePassword.InvalidCurrentPassword", "Mevcut şifre hatalı.");

        if (_hasher.Verify(request.NewPassword, user.PasswordHash))
            return Result.Failure(
                "Auth.ChangePassword.SameAsCurrent",
                "Yeni şifre mevcut şifre ile aynı olamaz.");

        var newHash = _hasher.Hash(request.NewPassword);
        user.UpdatePasswordHash(newHash);

        await _users.SaveChangesAsync(ct);

        return Result.Success();
    }
}
