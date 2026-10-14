using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Planlanan hatırlatma / vade tarihi: geçmiş kayıtlar ve uzun vadeli plan için daha geniş pencere.
/// </summary>
internal static class VaccinationDueAtWindow
{
    /// <summary>Planlanabilecek en uzak gelecek (yıl); içe aktarma doğrulaması da bu sınırı kullanır.</summary>
    public const int MaxFutureYears = 5;

    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime dueAtUtc)
    {
        var now = DateTime.UtcNow;
        if (dueAtUtc < now.AddYears(-10))
        {
            return Result.Failure(
                "Vaccinations.DueTooFarInPast",
                "Plan tarihi 10 yıldan daha eski olamaz.");
        }

        return ValidateFutureLimit(dueAtUtc, now);
    }

    /// <summary>
    /// Yalnız gelecek sınırı. İçe aktarılan geçmiş aşıda geçmiş sınırı yoktur (eski kayıtların sonraki tarihi geçmişte
    /// kalabilir; IMPORT_API_CONTRACT.md §4.3), gelecek sınırı günlük girişle aynıdır.
    /// </summary>
    public static Result ValidateFutureLimit(DateTime dueAtUtc, DateTime nowUtc)
    {
        if (dueAtUtc > nowUtc.AddYears(MaxFutureYears))
        {
            return Result.Failure(
                "Vaccinations.DueTooFarInFuture",
                "Plan tarihi en fazla 5 yıl ileriye kaydedilebilir.");
        }

        return Result.Success();
    }
}
