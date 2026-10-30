using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Appointments.Availability;

/// <summary>
/// Müsaitlik verisini yükleyip <see cref="AppointmentAvailabilityCalculator"/>'a verir (portal, bağlantı ve personel uçları,
/// talep oluşturma ve onay ortak). Hekimlerin başka kliniklerdeki randevuları da hekimi meşgul sayar.
/// </summary>
public sealed class AppointmentAvailabilityService
{
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;
    private readonly IReadRepository<ClinicAppointmentSettings> _appointmentSettings;
    private readonly IClinicVeterinarianReader _veterinarians;

    public AppointmentAvailabilityService(
        IReadRepository<Appointment> appointments,
        IReadRepository<ClinicWorkingHour> workingHours,
        IReadRepository<ClinicAppointmentSettings> appointmentSettings,
        IClinicVeterinarianReader veterinarians)
    {
        _appointments = appointments;
        _workingHours = workingHours;
        _appointmentSettings = appointmentSettings;
        _veterinarians = veterinarians;
    }

    public async Task<IReadOnlyList<AvailabilityDay>> GetDaysAsync(
        Guid tenantId,
        Guid clinicId,
        AppointmentReason reason,
        DateOnly fromLocalDate,
        int days,
        DateTime earliestUtc,
        DateTime latestUtc,
        CancellationToken ct)
    {
        var input = await BuildInputAsync(tenantId, clinicId, reason, reason.DefaultDurationMinutes, fromLocalDate, days, earliestUtc, latestUtc, ct);
        return AppointmentAvailabilityCalculator.Compute(input);
    }

    /// <summary>Talep edilen başlangıç müsait listede mi (online talep yalnız gösterilen saatlere yapılır).</summary>
    public async Task<AvailableSlot?> FindSlotAsync(
        Guid tenantId,
        Guid clinicId,
        AppointmentReason reason,
        DateTime startUtc,
        DateTime earliestUtc,
        DateTime latestUtc,
        CancellationToken ct)
    {
        var day = await GetDaysAsync(
            tenantId, clinicId, reason, OperationDayBounds.ToLocalDate(startUtc), 1, earliestUtc, latestUtc, ct);
        return day.SelectMany(d => d.Slots).FirstOrDefault(s => s.StartUtc == startUtc);
    }

    /// <summary>
    /// Verilen aralıkta boş uygun hekimler (onayda hekim seçilmezse otomatik atama için). Çalışma saati denetlenmez; randevu
    /// oluşturma kuralı denetler. <paramref name="hasVeterinarians"/>: klinikte uygun hekim tanımlı mı.
    /// </summary>
    public async Task<(bool HasVeterinarians, IReadOnlyList<Guid> Free)> FindFreeVeterinariansAsync(
        Guid tenantId,
        Guid clinicId,
        AppointmentReason? reason,
        DateTime startUtc,
        int durationMinutes,
        CancellationToken ct)
    {
        var endUtc = startUtc.AddMinutes(durationMinutes);
        var input = await BuildInputAsync(
            tenantId, clinicId, reason, durationMinutes, OperationDayBounds.ToLocalDate(startUtc), 1, startUtc, startUtc, ct);
        if (input.EligibleVeterinarianIds.Count == 0)
            return (false, []);

        // Hekim seçimi için klinik üst üste binme kuralı yok sayılır; o kural randevu oluşturmada ayrıca denetlenir.
        var vetOnly = input with { AllowOverlap = true };
        return AppointmentAvailabilityCalculator.TryFindFreeVeterinarians(vetOnly, startUtc, endUtc, out var free)
            ? (true, free)
            : (true, []);
    }

    private async Task<AvailabilityInput> BuildInputAsync(
        Guid tenantId,
        Guid clinicId,
        AppointmentReason? reason,
        int durationMinutes,
        DateOnly fromLocalDate,
        int days,
        DateTime earliestUtc,
        DateTime latestUtc,
        CancellationToken ct)
    {
        var hours = await _workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, clinicId), ct);
        var settings = await _appointmentSettings.FirstOrDefaultAsync(new ClinicAppointmentSettingsByClinicSpec(tenantId, clinicId), ct);
        var defaults = ClinicAppointmentSettingsDefaults.Build();

        var vets = (await _veterinarians.ListAsync(tenantId, clinicId, ct))
            .Select(v => v.UserId)
            .Where(id => reason is null || reason.AllowsVeterinarian(id))
            .ToList();

        var fromUtc = OperationDayBounds.ForLocalDate(fromLocalDate).DayStartUtc;
        var toUtc = OperationDayBounds.ForLocalDate(fromLocalDate.AddDays(Math.Clamp(days, 1, AppointmentAvailabilityCalculator.MaxDays))).DayStartUtc;
        var busy = await _appointments.ListAsync(new AppointmentsForAvailabilitySpec(tenantId, clinicId, vets, fromUtc, toUtc), ct);

        return new AvailabilityInput(
            clinicId,
            fromLocalDate,
            days,
            ClinicWorkingHoursDefaults.Effective(hours),
            durationMinutes,
            settings?.SlotIntervalMinutes ?? defaults.SlotIntervalMinutes,
            settings?.AllowOverlappingAppointments ?? defaults.AllowOverlappingAppointments,
            vets,
            busy.Select(a => new BusyInterval(a.ClinicId, a.VeterinarianUserId, a.ScheduledAtUtc, a.ScheduledEndUtc)).ToList(),
            earliestUtc,
            latestUtc);
    }
}

/// <summary>Aralıkla çakışan planlı randevular: bu klinikte olanlar ve uygun hekimlerin (başka klinikte olsa da) randevuları.</summary>
public sealed class AppointmentsForAvailabilitySpec : Specification<Appointment>
{
    public AppointmentsForAvailabilitySpec(
        Guid tenantId,
        Guid clinicId,
        IReadOnlyCollection<Guid> veterinarianUserIds,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var vetIds = veterinarianUserIds.ToArray();
        Query.AsNoTracking().Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < toUtc
            && fromUtc < a.ScheduledAtUtc.AddMinutes(a.DurationMinutes)
            && (a.ClinicId == clinicId
                || (a.VeterinarianUserId != null && vetIds.Contains(a.VeterinarianUserId.Value))));
    }
}
