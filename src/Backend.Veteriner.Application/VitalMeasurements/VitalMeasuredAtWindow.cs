using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.VitalMeasurements;

/// <summary>
/// Ölçüm zamanı penceresi: diğer klinik kayıtlar gibi en çok 7 gün geriye; ölçüm gelecekte olamaz (istemci saat kayması için 5 dk pay).
/// </summary>
internal static class VitalMeasuredAtWindow
{
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static Result Validate(DateTime measuredAtUtc, DateTime utcNow)
    {
        if (measuredAtUtc < utcNow.AddDays(-7))
        {
            return Result.Failure(
                "VitalMeasurements.MeasuredAtTooFarInPast",
                "Ölçüm zamanı çok eski; en fazla 7 gün öncesine kadar kayıt açılabilir.");
        }

        if (measuredAtUtc > utcNow.Add(FutureTolerance))
            return Result.Failure("VitalMeasurements.MeasuredAtInFuture", "Ölçüm zamanı gelecekte olamaz.");

        return Result.Success();
    }
}
