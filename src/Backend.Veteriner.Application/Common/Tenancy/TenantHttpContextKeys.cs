namespace Backend.Veteriner.Application.Common.Tenancy;

/// <summary>HTTP isteği Items sözlüğü anahtarı; API middleware ile Infrastructure TenantContext hizalanır.</summary>
public static class TenantHttpContextKeys
{
    public const string ResolvedTenantId = "__veteriner.ResolvedTenantId";
}
