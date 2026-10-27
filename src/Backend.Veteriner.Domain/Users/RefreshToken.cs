namespace Backend.Veteriner.Domain.Users;

public sealed class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    /// <summary>Oturumun bağlı olduğu kiracı; refresh ile aynı tenant korunur.</summary>
    public Guid? TenantId { get; private set; }
    /// <summary>Oturumun bağlı olduğu aktif klinik (opsiyonel).</summary>
    public Guid? ClinicId { get; private set; }
    public string TokenHash { get; private set; } = default!;   // raw token değil, SHA-256

    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? LastUsedAtUtc { get; private set; }         // ✅ session kullanımı
    public DateTime? RevokedAtUtc { get; private set; }

    public string? RevokeReason { get; private set; }            // ✅ denetim izi
    public string? ReplacedByTokenHash { get; private set; }     // rotation zinciri

    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    // FK
    public User User { get; private set; } = default!;

    private RefreshToken() { }

    public RefreshToken(
        Guid userId,
        string tokenHash,
        DateTime expiresAtUtc,
        string? ip,
        string? ua,
        DateTime utcNow,
        Guid? tenantId = null,
        Guid? clinicId = null)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = utcNow;
        IpAddress = ip;
        UserAgent = ua;
        TenantId = tenantId;
        ClinicId = clinicId;
    }

    public bool IsActiveAt(DateTime utcNow) => RevokedAtUtc is null && utcNow < ExpiresAtUtc;

    public void MarkUsed(DateTime utcNow)
    {
        if (RevokedAtUtc is not null)
            throw new InvalidOperationException("Revoked refresh token kullanılamaz.");

        LastUsedAtUtc = utcNow;
    }

    public void Revoke(DateTime utcNow, string? reason = null)
    {
        if (RevokedAtUtc is not null) return;

        RevokedAtUtc = utcNow;
        RevokeReason = string.IsNullOrWhiteSpace(reason) ? "revoked" : reason;
    }

    public void ReplaceWith(string newTokenHash, DateTime utcNow)
    {
        ReplacedByTokenHash = newTokenHash;
        Revoke(utcNow, "rotated");
    }

    public void BindClinic(Guid? clinicId)
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId gecersiz.", nameof(clinicId));
        ClinicId = clinicId;
    }
}
