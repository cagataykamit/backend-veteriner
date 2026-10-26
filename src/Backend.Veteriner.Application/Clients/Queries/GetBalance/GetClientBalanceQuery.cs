using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetBalance;

public sealed record GetClientBalanceQuery(Guid Id)
    : IRequest<Result<ClientBalanceDto>>;
