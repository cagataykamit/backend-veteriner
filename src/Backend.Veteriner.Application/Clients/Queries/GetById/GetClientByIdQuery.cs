using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetById;

public sealed record GetClientByIdQuery(Guid Id) : IRequest<Result<ClientDetailDto>>;
