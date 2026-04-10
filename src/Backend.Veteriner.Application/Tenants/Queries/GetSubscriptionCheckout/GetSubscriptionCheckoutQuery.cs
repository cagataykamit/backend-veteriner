using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionCheckout;

public sealed record GetSubscriptionCheckoutQuery(Guid TenantId, Guid CheckoutSessionId)
    : IRequest<Result<SubscriptionCheckoutSessionDto>>;

