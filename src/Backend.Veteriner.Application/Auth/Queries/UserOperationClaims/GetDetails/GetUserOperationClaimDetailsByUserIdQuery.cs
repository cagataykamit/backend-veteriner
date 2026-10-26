using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetDetails;

public sealed record GetUserOperationClaimDetailsByUserIdQuery(Guid UserId)
    : IRequest<IReadOnlyList<UserOperationClaimDetailDto>>;
