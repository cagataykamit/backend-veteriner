using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetMemberById;

/// <param name="TenantId">Route <c>tenantId</c>; JWT <c>tenant_id</c> ile eşleşmeli.</param>
/// <param name="MemberId">Route <c>memberId</c> (<see cref="Backend.Veteriner.Domain.Users.User"/> <c>Id</c>).</param>
public sealed record GetTenantMemberByIdQuery(Guid TenantId, Guid MemberId)
    : IRequest<Result<TenantMemberDetailDto>>;
