using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Uygulama zamanı: muayene ile aynı pencere (geç kayıt / makul ileri).
/// </summary>
internal static class VaccinationAppliedAtWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime appliedAtUtc)
    {
        var now = DateTime.UtcNow;
        if (appliedAtUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Vaccinations.AppliedTooFarInPast",
                "Uygulama zamanı çok eski; en fazla 7 gün öncesine kadar kayıt açılabilir.");
        }

        if (appliedAtUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Vaccinations.AppliedTooFarInFuture",
                "Uygulama zamanı en fazla 2 yıl ileriye kaydedilebilir.");
        }

        return Result.Success();
    }
}
