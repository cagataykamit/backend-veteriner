using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetRecentSummary;

public sealed record GetClientRecentSummaryQuery(Guid ClientId)
    : IRequest<Result<ClientRecentSummaryDto>>;
