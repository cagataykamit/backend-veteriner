using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Create;

public sealed class CreatePermissionCommandHandler : IRequestHandler<CreatePermissionCommand, Result<Guid>>
{
    private readonly IPermissionRepository _repo;
    public CreatePermissionCommandHandler(IPermissionRepository repo) => _repo = repo;

    public async Task<Result<Guid>> Handle(CreatePermissionCommand cmd, CancellationToken ct)
    {
        var code = cmd.Code.Trim();

        if (await _repo.ExistsByCodeAsync(code, ct))
        {
            return Result<Guid>.Failure(
                "Permissions.DuplicateCode",
                $"Permission already exists: {code}");
        }

        var entity = new Permission(code, cmd.Description);
        await _repo.AddAsync(entity, ct);

        return Result<Guid>.Success(entity.Id);
    }
}
