using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Examinations;

/// <summary>
/// Muayene zamanı için oluşturma penceresi (randevu ile aynı mantık: geç kayıt / makul ileri plan).
/// </summary>
internal static class ExaminationExaminedAtWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime examinedAtUtc)
    {
        var now = DateTime.UtcNow;
        if (examinedAtUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Examinations.ExaminedTooFarInPast",
                "Muayene zamanı çok eski; en fazla 7 gün öncesine kadar kayıt açılabilir.");
        }

        if (examinedAtUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Examinations.ExaminedTooFarInFuture",
                "Muayene zamanı en fazla 2 yıl ileriye kaydedilebilir.");
        }

        return Result.Success();
    }
}
