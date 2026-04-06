using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary;

public sealed record GetTenantSubscriptionSummaryQuery(Guid TenantId)
    : IRequest<Result<TenantSubscriptionSummaryDto>>;
