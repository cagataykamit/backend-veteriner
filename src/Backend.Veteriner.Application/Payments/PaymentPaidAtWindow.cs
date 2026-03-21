using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Payments;

/// <summary>
/// Ödeme zamanı penceresi (muayene / tahsilat için tutarlı: geç kayıt, makul ileri).
/// </summary>
internal static class PaymentPaidAtWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

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
