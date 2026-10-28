namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>Tek kullanımlık e-posta kodunun amacı; bir amaç için üretilen kod diğerinde geçmez.</summary>
public enum OwnerPortalCodePurpose
{
    SignIn = 0,
    PasswordReset = 1,

    /// <summary>Oturumdaki hesabın silinmesini onaylar (ADR-041 K2).</summary>
    AccountDeletion = 2,
}
