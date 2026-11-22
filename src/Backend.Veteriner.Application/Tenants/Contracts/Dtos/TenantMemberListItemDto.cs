namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Kiracı üye listesi (tenant-scoped); global admin kullanıcı listesinden farklıdır.
/// <para>
/// <c>Name</c>: kullanıcının görünen adı, yoksa e-postadan türetilen ad
/// (<see cref="Backend.Veteriner.Application.Users.Common.UserDisplayName"/>).
/// Değer boş olabilir; frontend <c>name ?? email</c> şeklinde güvenli gösterim uygulamalıdır.
/// </para>
/// <para>
/// <c>Roles</c>: davetle atanabilir rol adları (detaydaki rol kümesiyle aynı süzme).
/// <c>Clinics</c>: üyenin bu kurumdaki aktif klinik adları.
/// </para>
/// </summary>
public sealed record TenantMemberListItemDto(
    Guid UserId,
    string Email,
    string? Name,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Clinics);
