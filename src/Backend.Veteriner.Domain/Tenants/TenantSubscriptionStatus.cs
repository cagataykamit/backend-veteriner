namespace Backend.Veteriner.Domain.Tenants;

/// <summary>Kiracı abonelik durumu. ReadOnly ileri fazda enforcement ile hizalanır.</summary>
public enum TenantSubscriptionStatus
{
    Trialing = 0,
    Active = 1,
    ReadOnly = 2,
    Cancelled = 3,
}
