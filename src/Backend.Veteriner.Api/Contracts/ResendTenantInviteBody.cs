namespace Backend.Veteriner.Api.Contracts;

public sealed class ResendTenantInviteBody
{
    /// <summary>Opsiyonel; yoksa varsayılan süre (gün) uygulanır.</summary>
    public DateTime? ExpiresAtUtc { get; set; }
}
