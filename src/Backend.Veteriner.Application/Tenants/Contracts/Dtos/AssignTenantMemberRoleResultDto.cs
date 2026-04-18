namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli üyesine rol atama sonucu. <c>AlreadyAssigned = true</c> ise istek idempotent yorumlanmıştır
/// (ilişki zaten vardı; yeni kayıt eklenmedi ve cache düşürülmedi).
/// </summary>
public sealed record AssignTenantMemberRoleResultDto(
    Guid UserId,
    Guid OperationClaimId,
    string OperationClaimName,
    bool AlreadyAssigned);
