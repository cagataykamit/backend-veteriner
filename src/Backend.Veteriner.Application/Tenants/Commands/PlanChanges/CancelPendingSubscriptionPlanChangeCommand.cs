using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.PlanChanges;

public sealed record CancelPendingSubscriptionPlanChangeCommand(Guid TenantId)
    : IRequest<Result<PendingSubscriptionPlanChangeDto>>, ITransactionalRequest, IIgnoreTenantWriteSubscriptionGuard;
