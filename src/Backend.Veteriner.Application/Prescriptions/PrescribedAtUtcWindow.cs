using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Prescriptions;

/// <summary>
/// Prescription date window: same rules as treatment/examination clinical dates (7 days past, 2 years future).
/// </summary>
internal static class PrescribedAtUtcWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime prescribedAtUtc)
    {
        var now = DateTime.UtcNow;
        if (prescribedAtUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Prescriptions.DateTooFarInPast",
                "Prescription date is too far in the past; at most 7 days back is allowed.");
        }

        if (prescribedAtUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Prescriptions.DateTooFarInFuture",
                "Prescription date may be at most 2 years in the future.");
        }

        return Result.Success();
    }

    public static Result ValidateFollowUpNotBeforePrescription(DateTime prescribedAtUtc, DateTime? followUpDateUtc)
    {
        if (!followUpDateUtc.HasValue)
            return Result.Success();
        var follow = ToUtc(followUpDateUtc.Value);
        if (follow < prescribedAtUtc)
        {
            return Result.Failure(
                "Prescriptions.FollowUpBeforePrescription",
                "Follow-up date must not be before prescription date.");
        }

        return Result.Success();
    }
}
