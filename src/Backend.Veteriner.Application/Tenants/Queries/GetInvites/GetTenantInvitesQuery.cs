using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetInvites;

/// <param name="TenantId">Route <c>tenantId</c>.</param>
/// <param name="PageRequest"><c>Search</c> → e-posta içerir; <c>Sort</c>/<c>Order</c> işlenmez.</param>
/// <param name="Status">Opsiyonel durum filtresi (<see cref="TenantInviteStatus"/>).</param>
public sealed record GetTenantInvitesQuery(
    Guid TenantId,
    PageRequest PageRequest,
    TenantInviteStatus? Status)
    : IRequest<Result<PagedResult<TenantInviteListItemDto>>>;
