using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetById;

public sealed class GetUserOperationClaimByIdQueryHandler
    : IRequestHandler<GetUserOperationClaimByIdQuery, UserOperationClaimDto?>
{
    private readonly IUserOperationClaimRepository _repo;

    public GetUserOperationClaimByIdQueryHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public async Task<UserOperationClaimDto?> Handle(GetUserOperationClaimByIdQuery request, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(request.Id, ct);
        return entity is null
            ? null
            : new UserOperationClaimDto(entity.Id, entity.UserId, entity.OperationClaimId);
    }
}
