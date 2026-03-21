namespace Backend.Veteriner.Application.Common.Constants;

/// <summary>JWT ve çok kiracılı bağlam için özel claim türleri.</summary>
public static class VeterinerClaims
{
    /// <summary>Aktif kiracı kimliği (GUID string). Tek kiracı oturumu varsayımı.</summary>
    public const string TenantId = "tenant_id";
}
