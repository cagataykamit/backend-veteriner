namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Kiracı üye listesi (tenant-scoped); global admin kullanıcı listesinden farklıdır.
/// <para>
/// <c>Name</c> geçici display fallback'tir: <see cref="Backend.Veteriner.Domain.Users.User"/>
/// domain'inde henüz gerçek bir ad alanı yoktur (ayrıntı: §22.6 ve
/// <see cref="Backend.Veteriner.Application.Tenants.Common.TenantMemberDisplayName"/>).
/// Değer boş olabilir; frontend <c>name ?? email</c> şeklinde güvenli gösterim uygulamalıdır.
/// </para>
/// </summary>
public sealed record TenantMemberListItemDto(
    Guid UserId,
    string Email,
    string? Name,
    bool EmailConfirmed,
    DateTime CreatedAtUtc);
