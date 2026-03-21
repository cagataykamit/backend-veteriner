using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Planlanan hatırlatma / vade tarihi: geçmiş kayıtlar ve uzun vadeli plan için daha geniş pencere.
/// </summary>
internal static class VaccinationDueAtWindow
{
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

        if (dueAtUtc > now.AddYears(5))
        {
            return Result.Failure(
                "Vaccinations.DueTooFarInFuture",
                "Plan tarihi en fazla 5 yıl ileriye kaydedilebilir.");
        }

        return Result.Success();
    }
}
