using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Permissions.GetById;

public sealed class GetPermissionByIdQueryHandler
    : IRequestHandler<GetPermissionByIdQuery, Result<PermissionDto>>
{
    private readonly IPermissionRepository _repo;
    public GetPermissionByIdQueryHandler(IPermissionRepository repo) => _repo = repo;

    public async Task<Result<PermissionDto>> Handle(GetPermissionByIdQuery q, CancellationToken ct)
    {
        var p = await _repo.GetByIdAsync(q.Id, ct);
        if (p is null)
        {
            return Result<PermissionDto>.Failure(
                "Permissions.NotFound",
                "Permission not found.");
        }

        var dto = new PermissionDto(p.Id, p.Code, p.Description);
        return Result<PermissionDto>.Success(dto);
    }
}
