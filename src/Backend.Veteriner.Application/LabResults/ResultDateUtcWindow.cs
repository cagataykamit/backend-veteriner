using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.LabResults;

/// <summary>
/// Lab result date window: same rules as prescription/treatment clinical dates (7 days past, 2 years future).
/// </summary>
internal static class ResultDateUtcWindow
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime resultDateUtc)
    {
        var now = DateTime.UtcNow;
        if (resultDateUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "LabResults.DateTooFarInPast",
                "Result date is too far in the past; at most 7 days back is allowed.");
        }

        if (resultDateUtc > now.AddYears(2))
        {
            return Result.Failure(
                "LabResults.DateTooFarInFuture",
                "Result date may be at most 2 years in the future.");
        }

        return Result.Success();
    }
}
