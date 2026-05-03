using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMember;

/// <summary>Kullanıcıyı kiracı üyeliğinden çıkarır (UserTenant + tenant içi UserClinic + davet whitelist operation claim'leri).</summary>
public sealed record RemoveTenantMemberCommand(Guid TenantId, Guid MemberId)
    : IRequest<Result<RemoveTenantMemberResultDto>>;
