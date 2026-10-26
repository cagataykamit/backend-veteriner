using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetPaymentSummary;

public sealed record GetClientPaymentSummaryQuery(Guid Id)
    : IRequest<Result<ClientPaymentSummaryDto>>;
