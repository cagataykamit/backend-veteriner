namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Art arda başarısız giriş kilidi (SEC-001). Personel (<c>User</c>) ve hasta sahibi (<c>OwnerAccount</c>) hesapları
/// aynı kuralı kullanır; sayaç ve kilit zamanı varlıkta tutulur, kural yalnızca burada yaşar.
/// </summary>
public static class SignInLockout
{
    /// <summary>Art arda bu kadar başarısız giriş hesabı kilitler.</summary>
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    public static bool IsLockedOut(DateTime? lockedUntilUtc, DateTime utcNow)
        => lockedUntilUtc is { } until && ToUtc(utcNow) < until;

    /// <summary>
    /// Başarısız giriş sonrası yeni durum: sayaç artar, <see cref="MaxFailedAttempts"/>'a ulaşınca hesap <see cref="Duration"/>
    /// kilitlenir ve sayaç sıfırlanır. Kilitliyken durum değişmez (kilit uzamaz).
    /// </summary>
    public static (int FailedCount, DateTime? LockedUntilUtc) RecordFailure(int failedCount, DateTime? lockedUntilUtc, DateTime utcNow)
    {
        var now = ToUtc(utcNow);
        if (IsLockedOut(lockedUntilUtc, now))
            return (failedCount, lockedUntilUtc);

        var count = failedCount + 1;
        return count >= MaxFailedAttempts ? (0, now.Add(Duration)) : (count, lockedUntilUtc);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
