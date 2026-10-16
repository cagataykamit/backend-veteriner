namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// E-postayla gönderilen tek kullanımlık kod (ADR-031 K3; v0'da SMS yok, AK-6). Düz kod saklanmaz: özet, satır kimliğiyle
/// tuzlanır (uygulama katmanı hesaplar). Süre, deneme hakkı ve bekleme kuralı yalnızca burada yaşar.
/// </summary>
public sealed class OwnerPortalCode
{
    public const int Length = 6;
    public const int MaxFailedAttempts = 5;
    public const int CodeHashLength = 64;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Aynı amaç için bu süre dolmadan yeni kod üretilmez (e-posta bombardımanı ve deneme çoğaltma önlemi).</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public OwnerPortalCodePurpose Purpose { get; private set; }
    public string CodeHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }

    private OwnerPortalCode() { }

    /// <param name="id">Özet tuzu olarak kullanıldığı için özetten önce üretilir.</param>
    public static OwnerPortalCode Issue(
        Guid id,
        Guid ownerAccountId,
        OwnerPortalCodePurpose purpose,
        string codeHash,
        DateTime utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Kod kimliği geçersiz.", nameof(id));
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), "Kod amacı geçersiz.");
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("Kod özeti boş olamaz.", nameof(codeHash));

        var now = ToUtc(utcNow);
        return new OwnerPortalCode
        {
            Id = id,
            OwnerAccountId = ownerAccountId,
            Purpose = purpose,
            CodeHash = codeHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime),
        };
    }

    /// <summary>Doğrulanabilir: kullanılmamış, geçersiz kılınmamış, süresi ve deneme hakkı bitmemiş.</summary>
    public bool IsActive(DateTime utcNow) =>
        ConsumedAtUtc is null && InvalidatedAtUtc is null && FailedAttempts < MaxFailedAttempts && ToUtc(utcNow) < ExpiresAtUtc;

    public bool IsInResendCooldown(DateTime utcNow) => ToUtc(utcNow) - CreatedAtUtc < ResendCooldown;

    public void Invalidate(DateTime utcNow) => InvalidatedAtUtc ??= ToUtc(utcNow);

    /// <summary>
    /// Doğrulama denemesi. Eşleşmezse deneme sayılır, hak bitince kod geçersiz olur. Eşleşirse kod tüketilir (tek kullanımlık).
    /// Aktif olmayan kodda her zaman false.
    /// </summary>
    public bool TryConsume(bool codeMatches, DateTime utcNow)
    {
        var now = ToUtc(utcNow);
        if (!IsActive(now))
            return false;

        if (!codeMatches)
        {
            FailedAttempts++;
            if (FailedAttempts >= MaxFailedAttempts)
                InvalidatedAtUtc = now;
            return false;
        }

        ConsumedAtUtc = now;
        return true;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
