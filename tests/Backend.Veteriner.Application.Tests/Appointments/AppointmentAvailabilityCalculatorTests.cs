using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Application.Common.Time;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>
/// Müsaitlik kuralı (ADR-042 K3): çalışma saati ∩ uygun hekim − çakışan randevu. Tarihler İstanbul duvar saatiyle kurulur
/// (CI UTC'dedir); 2026-11-02 Pazartesi.
/// </summary>
public sealed class AppointmentAvailabilityCalculatorTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly Guid VetA = Guid.NewGuid();
    private static readonly Guid VetB = Guid.NewGuid();

    private static DateTime Local(DateOnly date, int hour, int minute = 0) => OperationDayBounds.FromLocal(date.ToDateTime(new TimeOnly(hour, minute)));

    private static AvailabilityInput Input(
        IReadOnlyList<ClinicWorkingHourDto>? week = null,
        int duration = 30,
        int interval = 15,
        bool allowOverlap = true,
        IReadOnlyCollection<Guid>? vets = null,
        IReadOnlyList<BusyInterval>? busy = null,
        int days = 1,
        DateOnly? from = null)
        => new(
            ClinicId,
            from ?? Monday,
            days,
            week ?? ClinicWorkingHoursDefaults.BuildWeek(),
            duration,
            interval,
            allowOverlap,
            vets ?? [VetA, VetB],
            busy ?? [],
            DateTime.MinValue,
            DateTime.MaxValue);

    private static IReadOnlyList<AvailableSlot> Slots(AvailabilityInput input) => AppointmentAvailabilityCalculator.Compute(input).SelectMany(d => d.Slots).ToList();

    [Fact]
    public void Default_Monday_Should_Offer_Half_Hour_Slots_From_Opening_To_Closing_In_Istanbul_Time()
    {
        var slots = Slots(Input());

        slots.First().StartUtc.Should().Be(new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc), "09:00 İstanbul = 06:00 UTC");
        slots.Last().EndUtc.Should().Be(Local(Monday, 18));
        slots.Should().HaveCount(18);
    }

    [Fact]
    public void Closed_Sunday_Should_Have_No_Slots_And_Break_Should_Be_Skipped()
    {
        Slots(Input(from: new DateOnly(2026, 11, 1))).Should().BeEmpty();

        var week = new List<ClinicWorkingHourDto>
        {
            new(DayOfWeek.Monday, false, new TimeOnly(9, 0), new TimeOnly(13, 0), new TimeOnly(11, 0), new TimeOnly(12, 0)),
        };
        Slots(Input(week)).Select(s => OperationDayBounds.ToLocal(s.StartUtc).Hour * 100 + OperationDayBounds.ToLocal(s.StartUtc).Minute)
            .Should().Equal(900, 930, 1000, 1030, 1200, 1230);
    }

    [Fact]
    public void Step_Should_Be_Duration_Rounded_Up_To_Slot_Interval()
    {
        var slots = Slots(Input(duration: 20, interval: 15));
        (slots[1].StartUtc - slots[0].StartUtc).Should().Be(TimeSpan.FromMinutes(30));
        slots.Should().OnlyContain(s => (s.EndUtc - s.StartUtc) == TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void Busy_Veterinarians_Should_Reduce_Capacity_And_Unassigned_Appointments_Consume_One()
    {
        var at = Local(Monday, 10);
        var oneBusy = Slots(Input(busy: [new BusyInterval(ClinicId, VetA, at, at.AddMinutes(30))]))
            .Single(s => s.StartUtc == at);
        oneBusy.FreeVeterinarianIds.Should().Equal(VetB);

        Slots(Input(busy:
        [
            new BusyInterval(ClinicId, VetA, at, at.AddMinutes(30)),
            new BusyInterval(Guid.NewGuid(), VetB, at, at.AddMinutes(30)),
        ])).Should().NotContain(s => s.StartUtc == at, "hekimin başka klinikteki randevusu da onu meşgul eder");

        Slots(Input(busy:
        [
            new BusyInterval(ClinicId, VetA, at, at.AddMinutes(30)),
            new BusyInterval(ClinicId, null, at, at.AddMinutes(30)),
        ])).Should().NotContain(s => s.StartUtc == at, "hekimsiz randevu bir kapasite tüketir");
    }

    [Fact]
    public void Clinic_Without_Overlap_Should_Block_Slot_For_Any_Same_Clinic_Appointment()
    {
        var at = Local(Monday, 11);
        Slots(Input(allowOverlap: false, busy: [new BusyInterval(ClinicId, VetA, at.AddMinutes(15), at.AddMinutes(45))]))
            .Should().NotContain(s => s.StartUtc == at || s.StartUtc == at.AddMinutes(30));

        Slots(Input(allowOverlap: false, vets: [], busy: [new BusyInterval(Guid.NewGuid(), null, at, at.AddMinutes(30))]))
            .Should().Contain(s => s.StartUtc == at, "başka kliniğin hekimsiz randevusu bu kliniği etkilemez");
    }

    [Fact]
    public void Request_Window_Should_Filter_Slots()
    {
        var input = Input(days: 2) with { EarliestUtc = Local(Monday, 12), LatestUtc = Local(Monday.AddDays(1), 10) };
        var slots = Slots(input);

        slots.First().StartUtc.Should().Be(Local(Monday, 12));
        slots.Last().StartUtc.Should().Be(Local(Monday.AddDays(1), 10));
    }
}
