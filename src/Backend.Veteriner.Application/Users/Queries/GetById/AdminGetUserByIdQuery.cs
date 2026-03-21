using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Users.Queries.GetById;

public sealed record AdminGetUserByIdQuery(Guid Id)
    : IRequest<Result<AdminUserDetailDto>>;
