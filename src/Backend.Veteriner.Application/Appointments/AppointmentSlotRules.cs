using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Appointments;

/// <summary>
/// Planlı randevunun saatte yer alabilmesi — tek yer: klinik içi oluşturma ve bekleme listesi teklifinin kabulü (TIER-018) kullanır.
/// Sıra korunur: slot hizası → klinik çakışması (üst üste binmeye izin yoksa) → hayvan çakışması → hekim (kliniğin aktif hekimi ve
/// boş) → çalışma saati. Çakışma denetimleri çalışma saatinden önce çalışır; aksi halde asıl çakışma hatası gölgelenir (Faz 4B-7).
/// </summary>
internal static class AppointmentSlotRules
{
    public static async Task<Result> EnsureBookableAsync(
        IReadRepository<Appointment> appointments,
        IReadRepository<ClinicWorkingHour> workingHours,
        AppointmentAssignment assignment,
        ClinicAppointmentSettings? settingsRow,
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? veterinarianUserId,
        DateTime startUtc,
        int durationMinutes,
        CancellationToken ct)
    {
        var defaults = ClinicAppointmentSettingsDefaults.Build();
        var slotIntervalMinutes = settingsRow?.SlotIntervalMinutes ?? defaults.SlotIntervalMinutes;
        var allowClinicOverlap = settingsRow?.AllowOverlappingAppointments ?? defaults.AllowOverlappingAppointments;
        var endUtc = startUtc.AddMinutes(durationMinutes);

        var slotAlignment = AppointmentSlotIntervalValidation.Validate(startUtc, slotIntervalMinutes);
        if (!slotAlignment.IsSuccess)
            return slotAlignment;

        if (!allowClinicOverlap)
        {
            var clinicBusy = await appointments.FirstOrDefaultAsync(
                new AppointmentOverlappingAtClinicSpec(tenantId, clinicId, startUtc, endUtc), ct);
            if (clinicBusy is not null)
            {
                return Result.Failure(
                    "Appointments.ClinicTimeConflict",
                    "Bu zaman aralığında klinikte başka bir planlı randevu var.");
            }
        }

        var petBusy = await appointments.FirstOrDefaultAsync(
            new AppointmentOverlappingForPetSpec(tenantId, petId, startUtc, endUtc), ct);
        if (petBusy is not null)
        {
            return Result.Failure(
                "Appointments.PetTimeConflict",
                "Bu zaman aralığında hayvanın başka bir planlı randevusu var.");
        }

        var vetCheck = await assignment.EnsureVeterinarianAsync(
            tenantId, clinicId, veterinarianUserId, null, startUtc, endUtc, null, ct);
        if (!vetCheck.IsSuccess)
            return vetCheck;

        var hoursRows = await workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, clinicId), ct);
        return AppointmentWorkingHoursValidation.Validate(startUtc, durationMinutes, hoursRows);
    }
}
