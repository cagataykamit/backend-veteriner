namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>Hasta sahibi portal hesabının durumu (ADR-031 K3).</summary>
public enum OwnerAccountStatus
{
    Active = 0,

    /// <summary>Hesap kapatıldı; giriş ve oturum yenileme reddedilir, kayıtlar silinmez.</summary>
    Disabled = 1,
}
