namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli için tek üye detayı (tenant-scoped).
/// <c>CreatedAtUtc</c> <c>UserTenant.CreatedAtUtc</c>'dir (kullanıcı oluşturma değil, kiracıya katılım).
/// <c>Roles</c> yalnız whitelist claim'lerini içerir; teknik/internal claim'ler gizlenir.
/// Üye bu kiracıya ait değilse endpoint 404 döner (farklı kiracının üyesi de dahil) — sızma yok.
/// </summary>
public sealed record TenantMemberDetailDto(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    IReadOnlyList<TenantMemberRoleDto> Roles,
    IReadOnlyList<TenantMemberClinicDto> Clinics);
