using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Treatments;

/// <summary>
/// Treatment date window: same rules as examination <c>ExaminedAtUtc</c> (7 days past, 2 years future).
/// </summary>
internal static class TreatmentDateUtcWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime treatmentDateUtc)
    {
        var now = DateTime.UtcNow;
        if (treatmentDateUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Treatments.DateTooFarInPast",
                "Treatment date is too far in the past; at most 7 days back is allowed.");
        }

        if (treatmentDateUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Treatments.DateTooFarInFuture",
                "Treatment date may be at most 2 years in the future.");
        }

        return Result.Success();
    }

    public static Result ValidateFollowUpNotBeforeTreatment(DateTime treatmentDateUtc, DateTime? followUpDateUtc)
    {
        if (!followUpDateUtc.HasValue)
            return Result.Success();
        var follow = ToUtc(followUpDateUtc.Value);
        if (follow < treatmentDateUtc)
        {
            return Result.Failure(
                "Treatments.FollowUpBeforeTreatment",
                "Follow-up date must not be before treatment date.");
        }

        return Result.Success();
    }
}
