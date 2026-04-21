using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Payments;

/// <summary>
/// Ödeme zamanı penceresi (muayene / tahsilat için tutarlı: geç kayıt, makul ileri).
/// </summary>
internal static class PaymentPaidAtWindow
{
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    /// <summary>
    /// <see cref="DateTime"/> değerini UTC'ye normalize eder.
    /// <list type="bullet">
    /// <item><description><see cref="DateTimeKind.Utc"/>: olduğu gibi döner.</description></item>
    /// <item><description><see cref="DateTimeKind.Local"/>: <see cref="DateTime.ToUniversalTime"/> ile dönüştürülür.</description></item>
    /// <item><description><see cref="DateTimeKind.Unspecified"/>: Europe/Istanbul yerel saat olarak yorumlanır ve UTC'ye çevrilir.
    ///   Bu, frontend tarafından ISO8601 timezone bilgisi olmadan (örn. <c>"2026-04-17T23:30:00"</c>)
    ///   gönderilen saatleri (TR kullanıcısının yerel saati) güvenle UTC'ye çevirir ve dashboard pencerelerinin
    ///   (§27.3) ödemeyi doğru gün bucket'ında yakalamasını sağlar. (Bkz. §12.5 — tahsilat saati TZ semantiği.)</description></item>
    /// </list>
    /// </summary>
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
                IstanbulTimeZone),
        };

    private static TimeZoneInfo ResolveIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }

    public static Result Validate(DateTime paidAtUtc)
    {
        var now = DateTime.UtcNow;
        if (paidAtUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Payments.PaidTooFarInPast",
                "Ödeme zamanı çok eski; en fazla 7 gün öncesine kadar kayıt açılabilir.");
        }

        if (paidAtUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Payments.PaidTooFarInFuture",
                "Ödeme zamanı en fazla 2 yıl ileriye kaydedilebilir.");
        }

        return Result.Success();
    }
}
