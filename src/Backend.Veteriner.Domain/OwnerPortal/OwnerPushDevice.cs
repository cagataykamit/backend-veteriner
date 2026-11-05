namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Hasta sahibinin push alacak cihazı (MOBILE-003; ADR-031 K4/K7): sağlayıcının (FCM) verdiği cihaz jetonu. Hesap düzeyindedir
/// (portal hesabı kiracıya bağlı değildir, AK-3); kiracıya özgü gönderim kuralı bildirimi üreten taraftadır. Jeton sistem
/// genelinde tektir: aynı cihazda başka hesap oturum açıp kaydederse jeton o hesaba geçer. Veri azaltma gereği cihaz adı,
/// model veya IP tutulmaz.
/// </summary>
public sealed class OwnerPushDevice
{
    /// <summary>FCM kayıt jetonları pratikte ~160 karakter; üst sınır benzersiz indeks anahtarına sığar.</summary>
    public const int MaxTokenLength = 512;

    /// <summary>Hesap başına en fazla cihaz; aşılınca en uzun süredir görülmeyen düşer.</summary>
    public const int MaxDevicesPerAccount = 10;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OwnerAccountId { get; private set; }
    public OwnerPushPlatform Platform { get; private set; }
    public string Token { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Son kayıt/yenileme zamanı (istemci her açılışta jetonu yeniden kaydeder).</summary>
    public DateTime LastSeenAtUtc { get; private set; }

    private OwnerPushDevice() { }

    public static OwnerPushDevice Create(Guid ownerAccountId, OwnerPushPlatform platform, string token, DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));
        var normalized = NormalizeToken(token)
                         ?? throw new ArgumentException($"Cihaz jetonu 1–{MaxTokenLength} karakter olmalı.", nameof(token));

        var now = ToUtc(utcNow);
        return new OwnerPushDevice
        {
            OwnerAccountId = ownerAccountId,
            Platform = platform,
            Token = normalized,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
        };
    }

    /// <summary>Aynı jetonun yeniden kaydı: hesap (cihazda oturum değiştiyse) ve platform güncellenir, görülme zamanı ilerler.</summary>
    public void Refresh(Guid ownerAccountId, OwnerPushPlatform platform, DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));

        OwnerAccountId = ownerAccountId;
        Platform = platform;
        LastSeenAtUtc = ToUtc(utcNow);
    }

    /// <summary>Jeton karşılaştırma ve saklama kuralı (trim); boş ya da sınırı aşan değer <c>null</c>.</summary>
    public static string? NormalizeToken(string? token)
    {
        var trimmed = token?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxTokenLength ? null : trimmed;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
