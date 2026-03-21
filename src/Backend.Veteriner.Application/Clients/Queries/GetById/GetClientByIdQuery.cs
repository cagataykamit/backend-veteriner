using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetById;

public sealed record GetClientByIdQuery(Guid TenantId, Guid Id) : IRequest<Result<ClientDetailDto>>;
