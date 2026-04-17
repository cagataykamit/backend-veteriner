using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetMembers;

/// <param name="TenantId">Route <c>tenantId</c>; JWT <c>tenant_id</c> ile eşleşmeli.</param>
/// <param name="PageRequest"><c>Search</c> → e-posta içerir (contains); <c>Sort</c>/<c>Order</c> işlenmez.</param>
public sealed record GetTenantMembersQuery(Guid TenantId, PageRequest PageRequest)
    : IRequest<Result<PagedResult<TenantMemberListItemDto>>>;
