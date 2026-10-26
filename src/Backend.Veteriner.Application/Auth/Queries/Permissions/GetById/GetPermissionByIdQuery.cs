using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.Permissions.GetById;

public sealed record GetPermissionByIdQuery(Guid Id)
    : IRequest<Result<PermissionDto>>;
