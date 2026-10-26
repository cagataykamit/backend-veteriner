using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Charges.Queries.GetAllocations;

public sealed record GetChargeAllocationsQuery(Guid ChargeId)
    : IRequest<Result<IReadOnlyList<ChargeAllocationItemDto>>>;
