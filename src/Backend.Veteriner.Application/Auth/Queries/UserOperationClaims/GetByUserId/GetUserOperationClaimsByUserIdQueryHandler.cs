using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetByUserId;

public sealed class GetUserOperationClaimsByUserIdQueryHandler
    : IRequestHandler<GetUserOperationClaimsByUserIdQuery, IReadOnlyList<UserOperationClaimDto>>
{
    private readonly IUserOperationClaimRepository _repo;

    public GetUserOperationClaimsByUserIdQueryHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public async Task<IReadOnlyList<UserOperationClaimDto>> Handle(GetUserOperationClaimsByUserIdQuery request, CancellationToken ct)
    {
        var list = await _repo.GetByUserIdAsync(request.UserId, ct);

        return list.Select(x => new UserOperationClaimDto(
            x.Id,
            x.UserId,
            x.OperationClaimId
        )).ToList();
    }
}
