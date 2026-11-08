using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clinics;

/// <summary>"Şu an açık" (TIER-015): açılış dahil, kapanış hariç, mola ve kapalı gün açık sayılmaz, satırı olmayan gün kapalı.</summary>
public sealed class ClinicWorkingHourOpenAtTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ClinicWorkingHour[] Week =
    [
        ClinicWorkingHour.Create(TenantId, ClinicId, DayOfWeek.Monday, false, new(9, 0), new(18, 0), new(12, 0), new(13, 0), Now),
        ClinicWorkingHour.Create(TenantId, ClinicId, DayOfWeek.Saturday, false, new(10, 0), new(14, 0), null, null, Now),
        ClinicWorkingHour.Create(TenantId, ClinicId, DayOfWeek.Sunday, true, null, null, null, null, Now),
    ];

    // 2026-11-09 Pazartesi, 2026-11-14 Cumartesi, 2026-11-15 Pazar, 2026-11-10 Salı (satır yok).
    [Theory]
    [InlineData("2026-11-09T09:00", true)]
    [InlineData("2026-11-09T08:59", false)]
    [InlineData("2026-11-09T11:59", true)]
    [InlineData("2026-11-09T12:00", false)]
    [InlineData("2026-11-09T12:59", false)]
    [InlineData("2026-11-09T13:00", true)]
    [InlineData("2026-11-09T17:59", true)]
    [InlineData("2026-11-09T18:00", false)]
    [InlineData("2026-11-14T10:30", true)]
    [InlineData("2026-11-14T14:00", false)]
    [InlineData("2026-11-15T11:00", false)]
    [InlineData("2026-11-10T11:00", false)]
    public void IsOpenAt_Should_Follow_The_Clinic_Week(string local, bool expected)
        => ClinicWorkingHour.IsOpenAt(Week, DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public void IsOpenAt_Should_Be_False_Without_Rows()
        => ClinicWorkingHour.IsOpenAt([], new DateTime(2026, 11, 9, 11, 0, 0)).Should().BeFalse();
}
