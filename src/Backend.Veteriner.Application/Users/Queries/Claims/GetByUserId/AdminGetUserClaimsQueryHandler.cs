using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Mediator;

namespace Backend.Veteriner.Application.Users.Queries.Claims.GetByUserId;

public sealed class AdminGetUserClaimsQueryHandler
    : IRequestHandler<AdminGetUserClaimsQuery, IReadOnlyList<UserOperationClaimDetailDto>>
{
    private readonly IUserOperationClaimRepository _repo;

    public AdminGetUserClaimsQueryHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public ValueTask<IReadOnlyList<UserOperationClaimDetailDto>> Handle(
        AdminGetUserClaimsQuery request,
        CancellationToken ct)
        => new(_repo.GetDetailsByUserIdAsync(request.UserId, ct));
}
