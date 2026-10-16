namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Portal oturumu (yenileme jetonu). Personel <c>RefreshToken</c> tablosundan ayrıdır (ayrı kapsam); ham değer saklanmaz.
/// Her yenilemede döndürülür; iptal edilmiş jetonun tekrar kullanımı hesabın tüm oturumlarını kapatır (handler).
/// Veri azaltma gereği IP/cihaz bilgisi tutulmaz.
/// </summary>
public sealed class OwnerPortalRefreshToken
{
    public const int TokenHashLength = 64;
    public const int MaxRevokeReasonLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OwnerAccountId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokeReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    private OwnerPortalRefreshToken() { }

    public static OwnerPortalRefreshToken Create(Guid ownerAccountId, string tokenHash, DateTime expiresAtUtc, DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token özeti boş olamaz.", nameof(tokenHash));

        return new OwnerPortalRefreshToken
        {
            OwnerAccountId = ownerAccountId,
            TokenHash = tokenHash,
            CreatedAtUtc = ToUtc(utcNow),
            ExpiresAtUtc = ToUtc(expiresAtUtc),
        };
    }

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsExpired(DateTime utcNow) => ToUtc(utcNow) >= ExpiresAtUtc;

    public void Revoke(string reason, DateTime utcNow)
    {
        if (IsRevoked)
            return;
        RevokedAtUtc = ToUtc(utcNow);
        RevokeReason = string.IsNullOrWhiteSpace(reason) ? "revoked" : reason.Trim();
    }

    /// <summary>Döndürme: yeni jeton özeti zincire yazılır, bu jeton kapanır.</summary>
    public void ReplaceWith(string newTokenHash, DateTime utcNow)
    {
        LastUsedAtUtc = ToUtc(utcNow);
        ReplacedByTokenHash = newTokenHash;
        Revoke("rotated", utcNow);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
