using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetById;

public sealed record GetUserOperationClaimByIdQuery(Guid Id)
    : IRequest<UserOperationClaimDto?>;
