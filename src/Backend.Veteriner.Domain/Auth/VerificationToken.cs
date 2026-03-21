namespace Backend.Veteriner.Domain.Auth;

public enum VerificationPurpose
{
    EmailVerify = 1,
    PasswordReset = 2 // ileride kullanaca��z
}

public sealed class VerificationToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public VerificationPurpose Purpose { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; private set; }

    // navigation (opsiyonel)
    public Users.User? User { get; private set; }

    private VerificationToken() { }

    public VerificationToken(Guid userId, string tokenHash, VerificationPurpose purpose, DateTime expiresAtUtc)
    {
        UserId = userId;
        TokenHash = tokenHash;
        Purpose = purpose;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsActive => UsedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;

    public void MarkUsed()
    {
        if (UsedAtUtc is null) UsedAtUtc = DateTime.UtcNow;
    }
}
