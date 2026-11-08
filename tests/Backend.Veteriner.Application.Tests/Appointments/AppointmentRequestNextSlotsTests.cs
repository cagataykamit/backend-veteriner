using Ardalis.Specification;
using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>
/// Herkese açık en yakın müsait saat (TIER-017): talep akışının müsaitliğini kullanır (dolu saat düşer), gün İstanbul takvimine göre
/// başlar, klinik gösterimi kapatınca hesap yapılmaz. Çalışma saati kaydı yok: varsayılan hafta (hafta içi 09:00–18:00).
/// 2026-11-02 Pazartesi.
/// </summary>
public sealed class AppointmentRequestNextSlotsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid VetId = Guid.NewGuid();
    private static readonly Clinic Clinic = new(TenantId, "Çağrı Veteriner", "İstanbul");

    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _workingHours = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _appointmentSettings = new();
    private readonly Mock<IClinicVeterinarianReader> _veterinarians = new();

    public AppointmentRequestNextSlotsTests()
    {
        _workingHours.Setup(r => r.ListAsync(It.IsAny<ISpecification<ClinicWorkingHour>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _veterinarians.Setup(r => r.ListAsync(TenantId, Clinic.Id, It.IsAny<CancellationToken>())).ReturnsAsync([new ClinicVeterinarianDto(VetId, "Dr. Ayşe")]);
        Busy();
    }

    private void Busy(params Appointment[] appointments)
        => _appointments.Setup(r => r.ListAsync(It.IsAny<ISpecification<Appointment>>(), It.IsAny<CancellationToken>())).ReturnsAsync(appointments.ToList());

    private static DateTime Local(int day, int hour, int minute = 0) => OperationDayBounds.FromLocal(new DateTime(2026, 11, day, hour, minute, 0));

    private AppointmentRequestIntake Intake(DateTime utcNow)
    {
        var availability = new AppointmentAvailabilityService(_appointments.Object, _workingHours.Object, _appointmentSettings.Object, _veterinarians.Object);
        var emails = new AppointmentRequestEmailComposer(
            new Mock<IReadRepository<ClinicAppointmentRequestEmailTemplate>>().Object,
            new Mock<IReadRepository<Clinic>>().Object,
            new Mock<IReadRepository<Pet>>().Object,
            new Mock<IEmailSender>().Object,
            new AppointmentOwnerLinkIssuer(
                new Mock<IOwnerPortalSecretGenerator>().Object,
                new Mock<ITokenHashService>().Object,
                Options.Create(new OwnerPortalOptions()),
                Options.Create(new AppOptions())));
        return new AppointmentRequestIntake(
            new Mock<IReadRepository<AppointmentReason>>().Object,
            new Mock<IReadRepository<AppointmentRequest>>().Object,
            new Mock<IRepository<AppointmentRequest>>().Object,
            availability,
            emails,
            new Mock<IUserNotificationPublisher>().Object,
            new FakeTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)));
    }

    private static OnlineBookingScope Scope(int minLeadMinutes = 0, bool show = true)
    {
        var settings = ClinicOnlineBookingSettings.Create(TenantId, Clinic.Id, "abcdefghjkmn", DateTime.UtcNow);
        settings.Update(true, minLeadMinutes, 30, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        settings.SetShowNextAvailableSlots(show, DateTime.UtcNow);
        return new OnlineBookingScope(TenantId, Clinic.Id, Clinic, settings);
    }

    [Fact]
    public async Task Should_Return_First_Free_Slots_And_Skip_Booked_One()
    {
        Busy(new Appointment(TenantId, Clinic.Id, Guid.NewGuid(), Local(2, 9), 30, veterinarianUserId: VetId));

        var slots = await Intake(Local(2, 8)).GetNextSlotsAsync(Scope(), AppointmentRequestIntake.DefaultNextSlotCount, default);

        slots.Should().HaveCount(6);
        slots.Select(s => s.StartUtc).Should().NotContain(Local(2, 9), "hekimin randevusu olan saat dolu");
        slots[0].StartUtc.Should().Be(Local(2, 9, 30));
        slots[0].Date.Should().Be(new DateOnly(2026, 11, 2));
        slots.Select(s => s.StartUtc).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Should_Start_From_Istanbul_Calendar_Day_Not_Utc_Day()
    {
        // 2026-11-02 21:30 UTC = Salı 00:30 İstanbul: UTC'de hâlâ pazartesi, ama pazartesinin saati kalmadı.
        var utcNow = new DateTime(2026, 11, 2, 21, 30, 0, DateTimeKind.Utc);

        var slots = await Intake(utcNow).GetNextSlotsAsync(Scope(), 3, default);

        slots.Should().HaveCount(3);
        slots[0].Date.Should().Be(new DateOnly(2026, 11, 3));
        slots[0].StartUtc.Should().Be(new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc), "salı 09:00 İstanbul = 06:00 UTC");
        slots.Should().OnlyContain(s => s.Date == OperationDayBounds.ToLocalDate(s.StartUtc));
    }

    [Fact]
    public async Task Should_Apply_Online_Lead_Time_And_Cap_Count()
    {
        var slots = await Intake(Local(2, 8)).GetNextSlotsAsync(Scope(minLeadMinutes: 120), 100, default);

        slots.Should().HaveCount(AppointmentRequestIntake.MaxNextSlotCount);
        slots[0].StartUtc.Should().Be(Local(2, 10), "en erken talep 2 saat sonrası");
    }

    [Fact]
    public async Task Should_Return_Nothing_Without_Computing_When_Clinic_Hides_Slots()
    {
        var slots = await Intake(Local(2, 8)).GetNextSlotsAsync(Scope(show: false), 6, default);

        slots.Should().BeEmpty();
        _workingHours.Verify(r => r.ListAsync(It.IsAny<ISpecification<ClinicWorkingHour>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
