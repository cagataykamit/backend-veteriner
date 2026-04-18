namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli üyesine klinik atama sonucu (Faz 4B). <c>AlreadyAssigned = true</c> ise istek idempotent yorumlanmıştır
/// (ilişki zaten vardı; yeni kayıt eklenmedi).
/// </summary>
public sealed record AssignTenantMemberClinicResultDto(
    Guid UserId,
    Guid ClinicId,
    string ClinicName,
    bool AlreadyAssigned);
