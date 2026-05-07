namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>Kiracı üye listesi için User Include yerine projeksiyon.</summary>
public sealed record TenantMemberListProjectionRow(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    DateTime CreatedAtUtc);
