using Ardalis.Specification;
using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>
/// Çalışma saati kısıtı kapalı klinik: günlük kapasite kapalı gün mantığı kullanmaz (her gün 08:00-20:00); online müsaitlik ayardan
/// etkilenmez, tanımlı haftaya göre sürer. Çalışma saati kaydı yok: varsayılan hafta (pazar kapalı). 2026-11-08 Pazar.
/// </summary>
public sealed class AppointmentAvailabilityWorkingHoursRestrictionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateOnly Sunday = new(2026, 11, 8);

    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _workingHours = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _appointmentSettings = new();
    private readonly Mock<IClinicVeterinarianReader> _veterinarians = new();

    public AppointmentAvailabilityWorkingHoursRestrictionTests()
    {
        _workingHours.Setup(r => r.ListAsync(It.IsAny<ISpecification<ClinicWorkingHour>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _appointments.Setup(r => r.ListAsync(It.IsAny<ISpecification<Appointment>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _veterinarians.Setup(r => r.ListAsync(TenantId, ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private AppointmentAvailabilityService Service(bool? enforceWorkingHours)
    {
        _appointmentSettings.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ClinicAppointmentSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(enforceWorkingHours is { } enforce
                ? ClinicAppointmentSettings.Create(TenantId, ClinicId, 30, 15, false, DateTime.UtcNow, enforceWorkingHours: enforce)
                : null);
        return new AppointmentAvailabilityService(_appointments.Object, _workingHours.Object, _appointmentSettings.Object, _veterinarians.Object);
    }

    [Fact]
    public async Task DailyCapacity_Should_Count_Closed_Day_When_Restriction_Disabled()
    {
        var capacity = await Service(enforceWorkingHours: false).GetDailyCapacityAsync(TenantId, ClinicId, false, Sunday, 2, CancellationToken.None);

        // 08:00-20:00, 30 dk randevu (adım süre kadar): 08:00 … 19:30 → 24 başlangıç, tek kaynak.
        capacity.Select(c => c.Capacity).Should().Equal(24, 24);
    }

    [Fact]
    public async Task DailyCapacity_Should_Keep_Closed_Day_When_Restriction_Enabled_Or_NoSettings()
    {
        (await Service(enforceWorkingHours: null).GetDailyCapacityAsync(TenantId, ClinicId, false, Sunday, 1, CancellationToken.None))
            .Single().Capacity.Should().Be(0);
        (await Service(enforceWorkingHours: true).GetDailyCapacityAsync(TenantId, ClinicId, false, Sunday, 1, CancellationToken.None))
            .Single().Capacity.Should().Be(0);
    }

    [Fact]
    public async Task OnlineAvailability_Should_Follow_Defined_Hours_When_Restriction_Disabled()
    {
        var days = await Service(enforceWorkingHours: false).GetDaysAsync(
            TenantId, ClinicId, null, 30, Sunday, 2, DateTime.MinValue, DateTime.MaxValue, CancellationToken.None);

        days.Single(d => d.LocalDate == Sunday).Slots.Should().BeEmpty("pazar tanımlı haftada kapalı");
        days.Single(d => d.LocalDate == Sunday.AddDays(1)).Slots.Should().NotBeEmpty();
    }
}
