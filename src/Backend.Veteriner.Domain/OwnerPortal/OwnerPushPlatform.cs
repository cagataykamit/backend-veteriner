namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>Push cihazının platformu (MOBILE-003). Sayısal değerler stabildir.</summary>
public enum OwnerPushPlatform
{
    Android = 0,
    Ios = 1,

    /// <summary>Tarayıcı (PWA web push).</summary>
    Web = 2,
}
