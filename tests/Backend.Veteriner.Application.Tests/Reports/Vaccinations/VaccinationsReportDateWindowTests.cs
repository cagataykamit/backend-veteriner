using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Vaccinations;

/// <summary>
/// <see cref="Backend.Veteriner.Application.Reports.Vaccinations.Specs.VaccinationsReportFilteredCountSpec"/>
/// (ve paged/export) ile aynı tarih penceresi — değişiklikte üç spec + bu test birlikte güncellenmeli.
/// </summary>
public sealed class VaccinationsReportDateWindowTests
{
    private static bool InReportWindow(Vaccination v, DateTime fromUtc, DateTime toUtc)
        => (v.Status == VaccinationStatus.Applied
            && v.AppliedAtUtc != null
            && v.AppliedAtUtc >= fromUtc
            && v.AppliedAtUtc <= toUtc)
        || (v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc != null
            && v.DueAtUtc >= fromUtc
            && v.DueAtUtc <= toUtc)
        || (v.Status == VaccinationStatus.Cancelled
            && v.DueAtUtc != null
            && v.DueAtUtc >= fromUtc
            && v.DueAtUtc <= toUtc);

    [Fact]
    public void Applied_Should_Use_AppliedAtUtc_Not_DueAt_For_Window()
    {
        var tid = Guid.NewGuid();
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var appliedIn = from.AddDays(3);
        var dueOut = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var v = new Vaccination(tid, Guid.NewGuid(), Guid.NewGuid(), null, "V1", VaccinationStatus.Applied, appliedIn, dueOut, null);
        InReportWindow(v, from, to).Should().BeTrue();

        var vWrongAxis = new Vaccination(tid, Guid.NewGuid(), Guid.NewGuid(), null, "V2", VaccinationStatus.Applied, null, from.AddDays(5), null);
        InReportWindow(vWrongAxis, from, to).Should().BeFalse("Applied için filtre yanlışlıkla Due üzerinden seçilmemeli.");
    }

    [Fact]
    public void Scheduled_Should_Use_DueAtUtc_Not_AppliedAt()
    {
        var tid = Guid.NewGuid();
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var dueIn = from.AddDays(10);
        var appliedMisleading = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var v = new Vaccination(tid, Guid.NewGuid(), Guid.NewGuid(), null, "V1", VaccinationStatus.Scheduled, appliedMisleading, dueIn, null);
        InReportWindow(v, from, to).Should().BeTrue();

        var vDueOut = new Vaccination(tid, Guid.NewGuid(), Guid.NewGuid(), null, "V2", VaccinationStatus.Scheduled, dueIn, from.AddYears(1), null);
        InReportWindow(vDueOut, from, to).Should().BeFalse();
    }

    [Fact]
    public void Cancelled_Should_Use_DueAtUtc()
    {
        var tid = Guid.NewGuid();
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var v = new Vaccination(tid, Guid.NewGuid(), Guid.NewGuid(), null, "V1", VaccinationStatus.Cancelled, null, from.AddDays(2), null);
        InReportWindow(v, from, to).Should().BeTrue();
    }
}
