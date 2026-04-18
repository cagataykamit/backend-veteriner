namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli üyesinden rol kaldırma sonucu. <c>AlreadyRemoved = true</c> ise istek idempotent yorumlanmıştır
/// (ilişki zaten yoktu; herhangi bir satır silinmedi ve cache düşürülmedi).
/// </summary>
public sealed record RemoveTenantMemberRoleResultDto(
    Guid UserId,
    Guid OperationClaimId,
    bool AlreadyRemoved);
