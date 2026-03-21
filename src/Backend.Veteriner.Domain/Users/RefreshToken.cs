namespace Backend.Veteriner.Domain.Users;

public sealed class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    /// <summary>Oturumun bağlı olduğu kiracı; refresh ile aynı tenant korunur.</summary>
    public Guid? TenantId { get; private set; }
    public string TokenHash { get; private set; } = default!;   // raw token değil, SHA-256

    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public DateTime? LastUsedAtUtc { get; private set; }         // ✅ session kullanımı
    public DateTime? RevokedAtUtc { get; private set; }

    public string? RevokeReason { get; private set; }            // ✅ denetim izi
    public string? ReplacedByTokenHash { get; private set; }     // rotation zinciri

    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    // FK
    public User User { get; private set; } = default!;

    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, DateTime expiresAtUtc, string? ip, string? ua, Guid? tenantId = null)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        IpAddress = ip;
        UserAgent = ua;
        TenantId = tenantId;
    }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;

    public void MarkUsed()
    {
        if (RevokedAtUtc is not null)
            throw new InvalidOperationException("Revoked refresh token kullanılamaz.");

        LastUsedAtUtc = DateTime.UtcNow;
    }

    public void Revoke(string? reason = null)
    {
        if (RevokedAtUtc is not null) return;

        RevokedAtUtc = DateTime.UtcNow;
        RevokeReason = string.IsNullOrWhiteSpace(reason) ? "revoked" : reason;
    }

    public void ReplaceWith(string newTokenHash)
    {
        ReplacedByTokenHash = newTokenHash;
        Revoke("rotated");
    }
}
