using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.PlanChanges;

public sealed record ScheduleSubscriptionDowngradeCommand(Guid TenantId, string TargetPlanCode, string? Reason)
    : IRequest<Result<PendingSubscriptionPlanChangeDto>>, ITransactionalRequest, IIgnoreTenantWriteSubscriptionGuard;
