using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetDetails;

public sealed class GetUserOperationClaimDetailsByUserIdQueryHandler
    : IRequestHandler<GetUserOperationClaimDetailsByUserIdQuery, IReadOnlyList<UserOperationClaimDetailDto>>
{
    private readonly IUserOperationClaimRepository _repo;

    public GetUserOperationClaimDetailsByUserIdQueryHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public ValueTask<IReadOnlyList<UserOperationClaimDetailDto>> Handle(GetUserOperationClaimDetailsByUserIdQuery request, CancellationToken ct)
        => new(_repo.GetDetailsByUserIdAsync(request.UserId, ct));
}
