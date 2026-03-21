using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetById;

public sealed record GetTenantByIdQuery(Guid Id) : IRequest<Result<TenantDetailDto>>;
