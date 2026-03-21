using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Appointments;

/// <summary>Oluşturma ve yeniden planlama için ortak UTC zaman penceresi kuralları.</summary>
internal static class AppointmentScheduleWindow
{
    public static Result Validate(DateTime scheduledUtc)
    {
        var now = DateTime.UtcNow;
        if (scheduledUtc < now.AddDays(-7))
        {
            return Result.Failure(
                "Appointments.ScheduledTooFarInPast",
                "Randevu zamanı çok eski; en fazla 7 gün öncesine kadar kayıt açılabilir.");
        }

        if (scheduledUtc > now.AddYears(2))
        {
            return Result.Failure(
                "Appointments.ScheduledTooFarInFuture",
                "Randevu zamanı en fazla 2 yıl ileriye planlanabilir.");
        }

        return Result.Success();
    }
}
