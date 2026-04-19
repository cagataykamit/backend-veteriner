namespace Backend.Veteriner.Api.Contracts;

/// <summary>
/// PUT /api/v1/tenants/{tenantId}/settings istek gövdesi. Route <c>tenantId</c> kaynak doğrudur;
/// gövdedeki <c>TenantId</c> isteğe bağlıdır ve doluysa route ile aynı olmalıdır.
/// </summary>
public sealed class UpdateTenantSettingsBody
{
    /// <summary>İsteğe bağlı; doluysa route tenantId ile aynı olmalıdır.</summary>
    public Guid? TenantId { get; init; }

    public string Name { get; init; } = default!;
}
