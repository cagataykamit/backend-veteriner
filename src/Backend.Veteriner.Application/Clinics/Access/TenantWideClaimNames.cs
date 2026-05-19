namespace Backend.Veteriner.Application.Clinics.Access;

/// <summary>
/// Tenant geneline erişim hakkı olan OperationClaim adları (whitelist).
/// <see cref="ClinicAssignmentAccessGuard"/>'ın read-scope semantiğinden farklı olarak burada
/// claim'i hiç olmayan kullanıcılar tenant-wide kabul EDİLMEZ; yalnız aşağıdaki adlardan biri
/// kullanıcının OperationClaim'leri arasında varsa <c>true</c> döner.
/// /me/clinics listesi ve /auth/select-clinic akışında UserClinic atamasını bypass etmek için
/// kullanılır.
/// </summary>
internal static class TenantWideClaimNames
{
    /// <summary>Tenant içi yönetici (tenant-scoped Admin OperationClaim adı).</summary>
    public const string TenantAdmin = "Admin";

    /// <summary>Tenant sahibi (Owner OperationClaim adı).</summary>
    public const string TenantOwner = "Owner";

    /// <summary>Platform yöneticisi (cross-tenant; <c>AdminClaimSeeder.PlatformAdminClaimName</c> ile aynı).</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>
    /// Verilen OperationClaim ad kümesinde tenant-wide rollerden en az biri var mı?
    /// Karşılaştırma <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </summary>
    public static bool IsTenantWide(IReadOnlyCollection<string>? operationClaimNames)
    {
        if (operationClaimNames is null || operationClaimNames.Count == 0)
            return false;

        foreach (var name in operationClaimNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (string.Equals(name, TenantAdmin, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, TenantOwner, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, PlatformAdmin, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
