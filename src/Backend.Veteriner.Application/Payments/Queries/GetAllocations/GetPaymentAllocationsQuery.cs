using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Payments.Queries.GetAllocations;

public sealed record GetPaymentAllocationsQuery(Guid PaymentId)
    : IRequest<Result<IReadOnlyList<PaymentAllocationItemDto>>>;
