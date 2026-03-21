using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Users.Queries.GetById;

public sealed class AdminGetUserByIdQueryHandler
    : IRequestHandler<AdminGetUserByIdQuery, Result<AdminUserDetailDto>>
{
    private readonly IUserReadRepository _users;

    public AdminGetUserByIdQueryHandler(IUserReadRepository users) => _users = users;

    public async Task<Result<AdminUserDetailDto>> Handle(AdminGetUserByIdQuery request, CancellationToken ct)
    {
        var user = await _users.FirstOrDefaultAsync(new UserByIdWithRolesSpec(request.Id), ct);
        if (user is null)
            return Result<AdminUserDetailDto>.Failure("Users.NotFound", "User not found.");

        var dto = new AdminUserDetailDto(
            user.Id,
            user.Email,
            user.EmailConfirmed,
            user.CreatedAtUtc,
            user.Roles.Select(r => r.Name).ToList()
        );
        return Result<AdminUserDetailDto>.Success(dto);
    }
}
