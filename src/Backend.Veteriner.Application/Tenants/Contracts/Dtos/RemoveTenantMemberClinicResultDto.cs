namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli üyesinden klinik kaldırma sonucu (Faz 4B). <c>AlreadyRemoved = true</c> ise istek idempotent yorumlanmıştır
/// (ilişki zaten yoktu; herhangi bir satır silinmedi).
/// </summary>
public sealed record RemoveTenantMemberClinicResultDto(
    Guid UserId,
    Guid ClinicId,
    bool AlreadyRemoved);
