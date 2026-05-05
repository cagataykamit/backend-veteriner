using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Appointments;

internal static class AppointmentSlotIntervalValidation
{
    public static Result Validate(DateTime scheduledAtUtc, int slotIntervalMinutes)
    {
        if (slotIntervalMinutes <= 0)
        {
            return Result.Failure(
                "Appointments.NotAlignedToSlotInterval",
                "Randevu saati klinik slot aralığına uygun değil.");
        }

        var local = AppointmentWorkingHoursValidation.ToClinicLocal(scheduledAtUtc);
        if (local.Second != 0 || local.Millisecond != 0)
        {
            return Result.Failure(
                "Appointments.NotAlignedToSlotInterval",
                "Randevu saati klinik slot aralığına uygun değil.");
        }

        var minutesSinceStartOfDay = (local.Hour * 60) + local.Minute;
        if (minutesSinceStartOfDay % slotIntervalMinutes != 0)
        {
            return Result.Failure(
                "Appointments.NotAlignedToSlotInterval",
                "Randevu saati klinik slot aralığına uygun değil.");
        }

        return Result.Success();
    }
}
