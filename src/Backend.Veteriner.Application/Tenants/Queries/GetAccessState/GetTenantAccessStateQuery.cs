using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAccessState;

public sealed record GetTenantAccessStateQuery(Guid TenantId)
    : IRequest<Result<TenantAccessStateDto>>;
