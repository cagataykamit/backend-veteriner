namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>Kiracı üye listesi (tenant-scoped); global admin kullanıcı listesinden farklıdır.</summary>
public sealed record TenantMemberListItemDto(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    DateTime CreatedAtUtc);
