using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using MediatR;

namespace Backend.Veteriner.Application.Users.Queries.Claims.GetByUserId;

public sealed class AdminGetUserClaimsQueryHandler
    : IRequestHandler<AdminGetUserClaimsQuery, IReadOnlyList<UserOperationClaimDetailDto>>
{
    private readonly IUserOperationClaimRepository _repo;

    public AdminGetUserClaimsQueryHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public Task<IReadOnlyList<UserOperationClaimDetailDto>> Handle(
        AdminGetUserClaimsQuery request,
        CancellationToken ct)
        => _repo.GetDetailsByUserIdAsync(request.UserId, ct);
}
