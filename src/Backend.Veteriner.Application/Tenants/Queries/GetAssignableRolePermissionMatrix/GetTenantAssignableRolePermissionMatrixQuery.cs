using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetAssignableRolePermissionMatrix;

public sealed record GetTenantAssignableRolePermissionMatrixQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>>>;
