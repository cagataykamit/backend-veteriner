using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetByUserId;

public sealed record GetUserOperationClaimsByUserIdQuery(Guid UserId)
    : IRequest<IReadOnlyList<UserOperationClaimDto>>;
