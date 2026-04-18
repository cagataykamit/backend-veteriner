namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Kiracı üyesinin bu tenant içindeki klinik özeti. Kaynak:
/// <c>IUserClinicRepository.ListAccessibleClinicsAsync(userId, tenantId, null)</c>.
/// </summary>
public sealed record TenantMemberClinicDto(
    Guid ClinicId,
    string Name,
    bool IsActive);
