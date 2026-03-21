namespace Backend.Veteriner.Application.Common.Outbox;

/// <summary>
/// Outbox mesaj tipleri için tek kaynak.
/// </summary>
public static class OutboxMessageTypes
{
    /// <summary>
    /// Canonical email outbox tipi.
    /// </summary>
    public const string Email = "Email";

    /// <summary>
    /// Geçmişte üretilmiş legacy varyant (geriye dönük uyumluluk için).
    /// </summary>
    public const string EmailLegacy = "email";
}

