using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

/// <param name="BillingPeriod">
/// Satın alınacak dönem (BILL-001): <c>Monthly</c> | <c>Yearly</c>, büyük/küçük harf duyarsız; sayısal değer geçersiz.
/// Boş: ücretli abonelikte mevcut dönem, hiç ödeme yoksa aylık.
/// </param>
public sealed record StartSubscriptionCheckoutCommand(Guid TenantId, string TargetPlanCode, string? BillingPeriod = null)
    : IRequest<Result<SubscriptionCheckoutSessionDto>>, IIgnoreTenantWriteSubscriptionGuard;
