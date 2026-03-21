using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.Create;

/// <summary>
/// Admin tarafından kullanıcı oluşturma komutu.
/// </summary>
public sealed class AdminCreateUserCommandHandler : IRequestHandler<AdminCreateUserCommand, Result<Guid>>
{
    private readonly IUserRepository _usersWrite;
    private readonly IReadRepository<User> _usersRead;
    private readonly IPasswordHasher _hasher;

    public AdminCreateUserCommandHandler(
        IUserRepository usersWrite,
        IReadRepository<User> usersRead,
        IPasswordHasher hasher)
    {
        _usersWrite = usersWrite;
        _usersRead = usersRead;
        _hasher = hasher;
    }

    public async Task<Result<Guid>> Handle(AdminCreateUserCommand request, CancellationToken ct)
    {
        // Aynı email var mı?
        var exists = await _usersRead.FirstOrDefaultAsync(new UserByEmailSpec(request.Email), ct);
        if (exists is not null)
            return Result<Guid>.Failure("Users.DuplicateEmail", "Bu e-posta adresi zaten kayıtlı.");

        var hash = _hasher.Hash(request.Password);

        // User constructor/factory sizde farklıysa uyarlayın
        var user = new User(request.Email, hash);

        await _usersWrite.AddAsync(user, ct);
        await _usersWrite.SaveChangesAsync(ct);

        return Result<Guid>.Success(user.Id);
    }
}
