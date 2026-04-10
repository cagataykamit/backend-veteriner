using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed record FinalizeSubscriptionCheckoutCommand(Guid TenantId, Guid CheckoutSessionId, string? ExternalReference = null)
    : IRequest<Result<SubscriptionCheckoutSessionDto>>, IIgnoreTenantWriteSubscriptionGuard;

