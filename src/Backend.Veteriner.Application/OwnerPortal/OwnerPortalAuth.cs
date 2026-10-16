namespace Backend.Veteriner.Application.OwnerPortal;

/// <summary>Portal kimlik şeması, politikası ve claim adları (ADR-031 K6). Personel şemasından ayrıdır.</summary>
public static class OwnerPortalAuth
{
    /// <summary>Portal uçlarının kimlik doğrulama şeması.</summary>
    public const string Scheme = "PortalBearer";

    /// <summary>Portal uçlarının yetki politikası: <see cref="Scheme"/> + <see cref="PrincipalTypeClaim"/> = <see cref="OwnerPrincipalType"/>.</summary>
    public const string Policy = "PortalOwner";

    public const string PrincipalTypeClaim = "principal_type";
    public const string OwnerPrincipalType = "owner";
}
