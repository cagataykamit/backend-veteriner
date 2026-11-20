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
/// Sıra korunur: slot hizası → klinik çakışması (hekimsiz randevuda, üst üste binmeye izin yoksa) → hayvan çakışması → hekim (kliniğin aktif hekimi ve
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

        var conflicts = await EnsureNoConflictAsync(
            appointments, assignment, allowClinicOverlap, tenantId, clinicId, petId, veterinarianUserId, null, startUtc, endUtc, null, ct);
        if (!conflicts.IsSuccess)
            return conflicts;

        var hoursRows = await workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, clinicId), ct);
        return AppointmentWorkingHoursValidation.Validate(startUtc, durationMinutes, hoursRows, settingsRow);
    }

    /// <summary>
    /// Klinik çakışması kuralı (kural tek yer): yalnız hekimsiz randevuda ve klinik üst üste binmeye kapalıysa klinikteki her planlı
    /// randevu saati doldurur. Hekimi atanmış randevuda çakışma hekim bazındadır; aynı saatte farklı hekimlerin randevusu çakışmaz
    /// (takvimde hekim sütunları, 2026-11-16 kararı). Müsaitlik hesabı da bu kuralı kullanır.
    /// </summary>
    public static bool ClinicOverlapBlocks(bool allowClinicOverlap, bool hasVeterinarian)
        => !allowClinicOverlap && !hasVeterinarian;

    /// <summary>
    /// Yalnız çakışmalar: klinik (<see cref="ClinicOverlapBlocks"/>) → hayvan → hekim. Oluşturma, düzenleme, yeniden zamanlama, teklif
    /// kabulü ve iptali geri alma kullanır; geri almada slot hizası ve çalışma saati yeniden denetlenmez (randevu bu kurallarla açılmıştı).
    /// </summary>
    /// <param name="keepVeterinarianUserId">Mevcut randevunun hekimi; hekim yetkisi kalkmış olsa da korunur.</param>
    /// <param name="excludeAppointmentId">Denetlenen randevunun kendisi.</param>
    public static async Task<Result> EnsureNoConflictAsync(
        IReadRepository<Appointment> appointments,
        AppointmentAssignment assignment,
        bool allowClinicOverlap,
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? veterinarianUserId,
        Guid? keepVeterinarianUserId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId,
        CancellationToken ct)
    {
        if (ClinicOverlapBlocks(allowClinicOverlap, veterinarianUserId is not null))
        {
            var clinicBusy = await appointments.FirstOrDefaultAsync(
                new AppointmentOverlappingAtClinicSpec(tenantId, clinicId, startUtc, endUtc, excludeAppointmentId), ct);
            if (clinicBusy is not null)
            {
                return Result.Failure(
                    "Appointments.ClinicTimeConflict",
                    "Bu zaman aralığında klinikte başka bir planlı randevu var.");
            }
        }

        var petBusy = await appointments.FirstOrDefaultAsync(
            new AppointmentOverlappingForPetSpec(tenantId, petId, startUtc, endUtc, excludeAppointmentId), ct);
        if (petBusy is not null)
        {
            return Result.Failure(
                "Appointments.PetTimeConflict",
                "Bu zaman aralığında hayvanın başka bir planlı randevusu var.");
        }

        return await assignment.EnsureVeterinarianAsync(
            tenantId, clinicId, veterinarianUserId, keepVeterinarianUserId, startUtc, endUtc, excludeAppointmentId, ct);
    }
}
