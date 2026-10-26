using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Queries.GetList;

public sealed record GetTenantsListQuery(PageRequest PageRequest)
    : IRequest<Result<PagedResult<TenantListItemDto>>>;
