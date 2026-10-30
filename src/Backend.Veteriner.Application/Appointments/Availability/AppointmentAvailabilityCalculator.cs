using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Appointments.Availability;

/// <summary>Müsaitlik hesabında dolu aralık: planlı randevu (klinik, varsa hekim).</summary>
public sealed record BusyInterval(Guid ClinicId, Guid? VeterinarianUserId, DateTime StartUtc, DateTime EndUtc);

/// <param name="EligibleVeterinarianIds">Neden için uygun hekimler; boşsa klinikte hekim yoktur ve klinik tek kaynak sayılır.</param>
/// <param name="AllowOverlap">Klinik randevu ayarı: kapalıysa klinikteki herhangi bir randevu saati doldurur.</param>
public sealed record AvailabilityInput(
    Guid ClinicId,
    DateOnly FromLocalDate,
    int Days,
    IReadOnlyList<ClinicWorkingHourDto> WeekHours,
    int DurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlap,
    IReadOnlyCollection<Guid> EligibleVeterinarianIds,
    IReadOnlyList<BusyInterval> Busy,
    DateTime EarliestUtc,
    DateTime LatestUtc);

/// <summary>Müsait başlangıç; <see cref="FreeVeterinarianIds"/> o saatte boş uygun hekimler (sahibe gösterilmez).</summary>
public sealed record AvailableSlot(DateTime StartUtc, DateTime EndUtc, IReadOnlyList<Guid> FreeVeterinarianIds);

public sealed record AvailabilityDay(DateOnly LocalDate, IReadOnlyList<AvailableSlot> Slots);

/// <summary>
/// Müsaitlik kuralı (ADR-042 K3) — tek yer, saf hesap: müsait saat = kliniğin çalışma saatleri (mola hariç) ∩ uygun hekimlerden
/// en az biri boş − çakışan planlı randevular. Saatler İstanbul duvar saatidir. Adım nedenin süresidir; klinik slot aralığına
/// hizalı kalması için süre aralığın katına yuvarlanır (randevu oluşturma kuralı slot hizası ister). Hekimi olmayan randevular
/// birer hekim kapasitesi tüketir. Bekleyen talepler saati bloke etmez.
/// </summary>
public static class AppointmentAvailabilityCalculator
{
    public const int MaxDays = 14;

    public static IReadOnlyList<AvailabilityDay> Compute(AvailabilityInput input)
    {
        var interval = Math.Max(1, input.SlotIntervalMinutes);
        var step = (int)Math.Ceiling(input.DurationMinutes / (double)interval) * interval;
        var days = new List<AvailabilityDay>();

        for (var offset = 0; offset < Math.Clamp(input.Days, 1, MaxDays); offset++)
        {
            var date = input.FromLocalDate.AddDays(offset);
            var row = input.WeekHours.FirstOrDefault(x => x.DayOfWeek == date.DayOfWeek);
            if (row is not { IsClosed: false, OpensAt: { } opensAt, ClosesAt: { } closesAt })
            {
                days.Add(new AvailabilityDay(date, []));
                continue;
            }

            var slots = new List<AvailableSlot>();
            var startMinute = (int)Math.Ceiling(opensAt.ToTimeSpan().TotalMinutes / interval) * interval;
            var closeMinute = (int)closesAt.ToTimeSpan().TotalMinutes;
            for (var minute = startMinute; minute + input.DurationMinutes <= closeMinute; minute += step)
            {
                var localStart = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
                var localEnd = localStart.AddMinutes(input.DurationMinutes);
                if (row.BreakStartsAt is { } bs && row.BreakEndsAt is { } be && localStart < be && bs < localEnd)
                    continue;

                var startUtc = OperationDayBounds.FromLocal(date.ToDateTime(localStart));
                if (startUtc < input.EarliestUtc || startUtc > input.LatestUtc)
                    continue;

                var endUtc = startUtc.AddMinutes(input.DurationMinutes);
                if (TryFindFreeVeterinarians(input, startUtc, endUtc, out var free))
                    slots.Add(new AvailableSlot(startUtc, endUtc, free));
            }

            days.Add(new AvailabilityDay(date, slots));
        }

        return days;
    }

    /// <summary>
    /// Aralık için kapasite: klinik üst üste binmeye kapalıysa klinikteki her randevu doldurur; uygun hekim yoksa klinik tek
    /// kaynaktır; aksi halde boş uygun hekim sayısı hekimsiz çakışan randevu sayısından fazla olmalı.
    /// </summary>
    public static bool TryFindFreeVeterinarians(
        AvailabilityInput input,
        DateTime startUtc,
        DateTime endUtc,
        out IReadOnlyList<Guid> freeVeterinarianIds)
    {
        freeVeterinarianIds = [];
        var overlapping = input.Busy.Where(b => b.StartUtc < endUtc && startUtc < b.EndUtc).ToList();
        var sameClinic = overlapping.Where(b => b.ClinicId == input.ClinicId).ToList();

        if (!input.AllowOverlap && sameClinic.Count > 0)
            return false;

        if (input.EligibleVeterinarianIds.Count == 0)
            return true;

        var busyVets = overlapping.Where(b => b.VeterinarianUserId is not null).Select(b => b.VeterinarianUserId!.Value).ToHashSet();
        var free = input.EligibleVeterinarianIds.Where(v => !busyVets.Contains(v)).ToList();
        var unassigned = sameClinic.Count(b => b.VeterinarianUserId is null);
        if (free.Count <= unassigned)
            return false;

        freeVeterinarianIds = free;
        return true;
    }
}
