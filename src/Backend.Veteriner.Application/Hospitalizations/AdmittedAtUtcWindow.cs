using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Hospitalizations;

/// <summary>
/// Admission instant window: same rules as lab/prescription clinical dates (7 days past, 2 years future).
/// </summary>
internal static class AdmittedAtUtcWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime admittedAtUtc)
    {
        var now = DateTime.UtcNow;
        if (admittedAtUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Hospitalizations.AdmittedAtTooFarInPast",
                "Admission time is too far in the past; at most 7 days back is allowed.");
        }

        if (admittedAtUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Hospitalizations.AdmittedAtTooFarInFuture",
                "Admission time may be at most 2 years in the future.");
        }

        return Result.Success();
    }
}
