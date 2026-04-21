namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Tenant paneli için tek üye detayı (tenant-scoped).
/// <c>CreatedAtUtc</c> <c>UserTenant.CreatedAtUtc</c>'dir (kullanıcı oluşturma değil, kiracıya katılım).
/// <c>Roles</c> yalnız whitelist claim'lerini içerir; teknik/internal claim'ler gizlenir.
/// Üye bu kiracıya ait değilse endpoint 404 döner (farklı kiracının üyesi de dahil) — sızma yok.
/// <para>
/// <c>Name</c> geçici display fallback'tir: <see cref="Backend.Veteriner.Domain.Users.User"/>
/// domain'inde henüz gerçek bir ad alanı yoktur (ayrıntı: §22.6 ve
/// <see cref="Backend.Veteriner.Application.Tenants.Common.TenantMemberDisplayName"/>).
/// Değer boş olabilir; frontend <c>name ?? email</c> şeklinde güvenli gösterim uygulamalıdır.
/// </para>
/// </summary>
public sealed record TenantMemberDetailDto(
    Guid UserId,
    string Email,
    string? Name,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    IReadOnlyList<TenantMemberRoleDto> Roles,
    IReadOnlyList<TenantMemberClinicDto> Clinics);
