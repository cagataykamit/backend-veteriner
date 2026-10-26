using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Queries.GetPendingPlanChange;

public sealed record GetPendingSubscriptionPlanChangeQuery(Guid TenantId) : IRequest<Result<PendingSubscriptionPlanChangeDto?>>;
