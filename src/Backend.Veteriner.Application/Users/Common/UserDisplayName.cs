using Backend.Veteriner.Application.Tenants.Common;

namespace Backend.Veteriner.Application.Users.Common;

/// <summary>
/// Kullanicinin gorunen adi: kayitli ad varsa o, yoksa e-postadan turetilen ad. Kural tek yerde yasar.
/// </summary>
public static class UserDisplayName
{
    public static string? Resolve(string? displayName, string? email)
        => string.IsNullOrWhiteSpace(displayName)
            ? TenantMemberDisplayName.DeriveFromEmail(email)
            : displayName.Trim();
}
