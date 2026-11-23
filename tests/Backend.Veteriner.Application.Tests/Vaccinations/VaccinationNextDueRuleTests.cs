using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations;

/// <summary>
/// Uygulanmış aşının sonraki uygulama tarihi (<c>Applied</c> + <c>DueAtUtc</c>) planlı aşı gibi vadelidir; aynı hayvana aynı aşı daha sonra
/// uygulandıysa karşılanmıştır ve hiçbir gecikmiş/yaklaşan görünümüne girmez (<see cref="VaccinationDueRule"/>). 1 takvim ayı penceresi aynen geçerli.
/// </summary>
public sealed class VaccinationNextDueRuleTests
{
    // 2026-10-16 15:00 UTC = İstanbul 18:00; bugünün başı İstanbul 2026-10-16 00:00 = 2026-10-15 21:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayStartUtc = new(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();
    private readonly Guid _definitionId = Guid.NewGuid();

    private Vaccination Applied(DateTime appliedUtc, DateTime? dueUtc, Guid? definitionId = null, Guid? clinicId = null)
        => new(_tenantId, _petId, clinicId ?? _clinicId, null, definitionId ?? _definitionId, "Karma", VaccinationStatus.Applied, appliedUtc, dueUtc, null, NowUtc);

    private Vaccination Imported(DateTime appliedUtc, DateTime? dueUtc)
        => Vaccination.CreateImported(_tenantId, _petId, _clinicId, _definitionId, "Karma", appliedUtc, dueUtc, null, Guid.NewGuid(), NowUtc);

    private static VaccinationDose Dose(Vaccination v) => new(v.Id, v.PetId, v.VaccineDefinitionId, v.VaccineName, v.AppliedAtUtc!.Value);

    [Fact]
    public void Applied_NextDue_Should_Be_Overdue_And_Upcoming_Like_Scheduled()
    {
        var overdueWindow = VaccinationDueRule.OverdueWindow(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, VaccinationDueRule.DashboardUpcomingDays);
        var overdue = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var upcoming = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(2), Guid.NewGuid());
        var noNextDue = Applied(TodayStartUtc.AddDays(-3), null, Guid.NewGuid());
        Vaccination[] rows = [overdue, upcoming, noNextDue];

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueWindow).Evaluate(rows).Should().ContainSingle().Which.Should().Be(overdue);
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to).Evaluate(rows).Should().ContainSingle().Which.Should().Be(upcoming);
        new VaccinationsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(overdue);
        new VaccinationsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, null, null, null, 1, 20, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(overdue);
        new OpenDueVaccinationsForPetSpec(_tenantId, _clinicId, _petId, overdueWindow.FromUtc, TodayStartUtc.AddDays(30), null)
            .Evaluate(rows).Should().Equal(overdue, upcoming);
        new NextDueVaccinationForPetSpec(_tenantId, _clinicId, _petId, overdueWindow.FromUtc, null)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(overdue);
    }

    [Fact]
    public void Fulfilled_NextDue_Should_Not_Count_Anywhere()
    {
        var overdueWindow = VaccinationDueRule.OverdueWindow(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, VaccinationDueRule.DashboardUpcomingDays);
        var overdue = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var upcoming = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(2));
        IReadOnlyCollection<Guid> fulfilled = [overdue.Id, upcoming.Id];
        Vaccination[] rows = [overdue, upcoming];

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueWindow, null, null, fulfilled).Evaluate(rows).Should().BeEmpty();
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to, null, null, fulfilled).Evaluate(rows).Should().BeEmpty();
        new VaccinationsFilteredCountSpec(
                _tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc, fulfilledNextDueIds: fulfilled)
            .Evaluate(rows).Should().BeEmpty();
        new OpenDueVaccinationsForPetSpec(_tenantId, _clinicId, _petId, overdueWindow.FromUtc, TodayStartUtc.AddDays(30), null, fulfilled)
            .Evaluate(rows).Should().BeEmpty();
        new NextDueVaccinationForPetSpec(_tenantId, _clinicId, _petId, overdueWindow.FromUtc, null, fulfilled).Evaluate(rows).Should().BeEmpty();
    }

    [Fact]
    public void Applied_NextDue_Older_Than_One_Month_Should_Not_Be_Overdue()
    {
        var window = VaccinationDueRule.OverdueWindow(NowUtc);
        var tooOld = Applied(TodayStartUtc.AddYears(-1), window.FromUtc.AddMinutes(-1));
        var oldest = Applied(TodayStartUtc.AddYears(-1), window.FromUtc, Guid.NewGuid());

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, window).Evaluate([tooOld, oldest]).Should().ContainSingle().Which.Should().Be(oldest);
    }

    [Fact]
    public void Imported_NextDue_Should_Count_Without_Conversion()
    {
        var imported = Imported(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-2));

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, VaccinationDueRule.OverdueWindow(NowUtc))
            .Evaluate([imported]).Should().ContainSingle();
        new NextDueSourcesSpec(_tenantId, _clinicId, null, null, null, null).Evaluate([imported]).Should().ContainSingle();
    }

    [Fact]
    public void FulfilledNextDueIds_Should_Use_Later_Dose_Of_Same_Vaccine_In_Any_Clinic()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var otherVaccineSource = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3), Guid.NewGuid());
        var laterInOtherClinic = Applied(TodayStartUtc.AddDays(-1), TodayStartUtc.AddYears(1), clinicId: Guid.NewGuid());

        VaccinationDueRule.FulfilledNextDueIds([Dose(source), Dose(otherVaccineSource)], [Dose(laterInOtherClinic)])
            .Should().BeEquivalentTo([source.Id]);
        new NextDueClosingDosesForPetsSpec(_tenantId, [_petId], source.AppliedAtUtc!.Value)
            .Evaluate([source, laterInOtherClinic]).Should().ContainSingle().Which.Id.Should().Be(laterInOtherClinic.Id);
    }

    [Fact]
    public void Cancelled_Later_Dose_Should_Not_Fulfil()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var cancelled = Applied(TodayStartUtc.AddDays(-1), null);
        cancelled.Cancel(Guid.NewGuid(), "Yanlış hastaya girildi.", NowUtc).IsSuccess.Should().BeTrue();

        new NextDueClosingDosesForPetsSpec(_tenantId, [_petId], source.AppliedAtUtc!.Value).Evaluate([cancelled]).Should().BeEmpty();
    }

    [Fact]
    public void Planned_Record_Should_Not_Be_Next_Due_Source()
    {
        var planned = new Vaccination(_tenantId, _petId, _clinicId, null, _definitionId, "Karma", VaccinationStatus.Scheduled, null, TodayStartUtc.AddDays(-2), null, NowUtc);

        new NextDueSourcesSpec(_tenantId, _clinicId, null, null, null, null).Evaluate([planned]).Should().BeEmpty();
    }

    [Fact]
    public async Task FulfilledNextDueIdsAsync_Should_Read_Sources_Then_Later_Doses()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var open = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-2), Guid.NewGuid());
        var later = Applied(TodayStartUtc.AddDays(-1), TodayStartUtc.AddYears(1));
        var repo = new Mock<IReadRepository<Vaccination>>();
        repo.Setup(r => r.ListAsync(It.IsAny<NextDueSourcesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dose(source), Dose(open)]);
        NextDueClosingDosesForPetsSpec? laterSpec = null;
        repo.Setup(r => r.ListAsync(It.IsAny<NextDueClosingDosesForPetsSpec>(), It.IsAny<CancellationToken>()))
            .Callback<ISpecification<Vaccination, VaccinationDose>, CancellationToken>((s, _) => laterSpec = (NextDueClosingDosesForPetsSpec)s)
            .ReturnsAsync([Dose(later)]);

        var ids = await repo.Object.FulfilledNextDueIdsAsync(_tenantId, _clinicId, null, VaccinationDueRule.OverdueWindow(NowUtc), CancellationToken.None);

        ids.Should().BeEquivalentTo([source.Id]);
        laterSpec.Should().NotBeNull();
        laterSpec!.Evaluate([source, later]).Should().ContainSingle().Which.Id.Should().Be(later.Id);
    }

    [Fact]
    public async Task FulfilledNextDueIdsAsync_Should_Skip_Second_Read_Without_Sources()
    {
        var repo = new Mock<IReadRepository<Vaccination>>();
        repo.Setup(r => r.ListAsync(It.IsAny<NextDueSourcesSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        (await repo.Object.FulfilledNextDueIdsAsync(_tenantId, _clinicId, null, VaccinationDueRule.OverdueWindow(NowUtc), CancellationToken.None))
            .Should().BeEmpty();
        repo.Verify(r => r.ListAsync(It.IsAny<NextDueClosingDosesForPetsSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private Vaccination Planned(DateTime dueUtc, Guid? definitionId = null, string name = "Karma")
        => new(_tenantId, _petId, _clinicId, null, definitionId ?? _definitionId, name, VaccinationStatus.Scheduled, null, dueUtc, null, NowUtc);

    /// <summary>Kaynakların karşılanmasını gerçek kapatma sorgusuyla hesaplar (<see cref="NextDueClosingDosesForPetsSpec"/>).</summary>
    private HashSet<Guid> Fulfilled(IReadOnlyList<Vaccination> sources, params Vaccination[] rows)
        => VaccinationDueRule.FulfilledNextDueIds(
            sources.Select(Dose),
            new NextDueClosingDosesForPetsSpec(_tenantId, [_petId], sources.Min(s => s.AppliedAtUtc!.Value)).Evaluate(rows).ToList());

    // 2026-11-23 kararı (b): aynı aşının planlı kaydı varsa önceki uygulamanın sonraki tarihi ayrıca vadeli sayılmaz.
    [Theory]
    [InlineData(-10)] // planlı, sonraki tarihten erken
    [InlineData(0)]
    [InlineData(25)] // planlı, sonraki tarihten geç
    public void Planned_Same_Vaccine_After_Application_Should_Close_NextDue(int plannedOffsetDays)
    {
        var nextDue = TodayStartUtc.AddDays(5);
        var source = Applied(TodayStartUtc.AddYears(-1), nextDue);
        var planned = Planned(nextDue.AddDays(plannedOffsetDays));

        Fulfilled([source], source, planned).Should().BeEquivalentTo([source.Id]);
    }

    [Fact]
    public void Without_Planned_Or_Later_Dose_NextDue_Should_Stay_Open()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(5));

        Fulfilled([source], source).Should().BeEmpty();
    }

    [Fact]
    public void Planned_Other_Vaccine_Should_Not_Close_NextDue()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(5));
        var otherVaccine = Planned(TodayStartUtc.AddDays(5), Guid.NewGuid(), "Kuduz");

        Fulfilled([source], source, otherVaccine).Should().BeEmpty();
    }

    [Fact]
    public void Cancelled_Planned_Should_Not_Close_NextDue()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(5));
        var cancelled = Planned(TodayStartUtc.AddDays(6));
        cancelled.Cancel(Guid.NewGuid(), "Sahibi randevuyu iptal etti.", NowUtc).IsSuccess.Should().BeTrue();

        Fulfilled([source], source, cancelled).Should().BeEmpty();
    }

    [Fact]
    public void Planned_Before_Application_Should_Not_Close_Later_NextDue()
    {
        // Uygulamadan önceye planlanmış (eski) kayıt bu uygulamanın sonraki tarihini kapatmaz.
        var source = Applied(TodayStartUtc.AddDays(-5), TodayStartUtc.AddYears(1));
        var stalePlanned = Planned(TodayStartUtc.AddDays(-20));

        Fulfilled([source], source, stalePlanned).Should().BeEmpty();
    }

    [Fact]
    public void Planned_Should_Count_Once_Where_NextDue_And_Planned_Overlap()
    {
        var overdueWindow = VaccinationDueRule.OverdueWindow(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, VaccinationDueRule.DashboardUpcomingDays);
        var overdueNextDue = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var overduePlanned = Planned(TodayStartUtc.AddDays(-2));
        var otherDefinitionId = Guid.NewGuid();
        var upcomingNextDue = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(2), otherDefinitionId);
        var upcomingPlanned = Planned(TodayStartUtc.AddDays(3), otherDefinitionId);
        Vaccination[] rows = [overdueNextDue, overduePlanned, upcomingNextDue, upcomingPlanned];
        IReadOnlyCollection<Guid> fulfilled = Fulfilled([overdueNextDue, upcomingNextDue], rows);

        fulfilled.Should().BeEquivalentTo([overdueNextDue.Id, upcomingNextDue.Id]);
        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueWindow, null, null, fulfilled)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(overduePlanned);
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to, null, null, fulfilled)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(upcomingPlanned);
        new VaccinationsFilteredCountSpec(
                _tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc, fulfilledNextDueIds: fulfilled)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(overduePlanned);
        new OpenDueVaccinationsForPetSpec(_tenantId, _clinicId, _petId, overdueWindow.FromUtc, TodayStartUtc.AddDays(30), null, fulfilled)
            .Evaluate(rows).Should().Equal(overduePlanned, upcomingPlanned);
        new NextDueVaccinationForPetSpec(_tenantId, _clinicId, _petId, overdueWindow.FromUtc, null, fulfilled)
            .Evaluate(rows).Should().ContainSingle().Which.Should().Be(overduePlanned);
    }

    [Fact]
    public void InMemory_ClosingDoseAtUtc_Should_Match_Expression()
    {
        var after = TodayStartUtc.AddYears(-1);
        var applied = Applied(TodayStartUtc.AddDays(-1), null);
        var planned = Planned(TodayStartUtc.AddDays(4));
        var cancelled = Planned(TodayStartUtc.AddDays(6));
        cancelled.Cancel(Guid.NewGuid(), "Sahibi randevuyu iptal etti.", NowUtc).IsSuccess.Should().BeTrue();
        var closes = VaccinationDueRule.ClosesNextDueAfter(after).Compile();
        var asDose = VaccinationDueRule.AsClosingDose.Compile();

        foreach (var v in new[] { applied, planned, cancelled })
        {
            var at = VaccinationDueRule.ClosingDoseAtUtc(v.Status, v.AppliedAtUtc, v.DueAtUtc);
            (at is { } a && a > after).Should().Be(closes(v));
        }

        asDose(applied).AtUtc.Should().Be(applied.AppliedAtUtc!.Value);
        asDose(planned).AtUtc.Should().Be(planned.DueAtUtc!.Value);
        closes(cancelled).Should().BeFalse();
    }

    [Fact]
    public void InMemory_IsOpenDue_Should_Match_Expression()
    {
        var open = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(2));
        var fulfilled = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(3), Guid.NewGuid());
        var noNextDue = Applied(TodayStartUtc.AddYears(-1), null, Guid.NewGuid());
        var set = new HashSet<Guid> { fulfilled.Id };
        var expr = VaccinationDueRule.IsOpenDue(set).Compile();

        foreach (var v in new[] { open, fulfilled, noNextDue })
            VaccinationDueRule.IsOpenDue(v.Id, v.Status, v.AppliedAtUtc, v.DueAtUtc, set).Should().Be(expr(v));
        expr(open).Should().BeTrue();
        expr(fulfilled).Should().BeFalse();
        expr(noNextDue).Should().BeFalse();
    }
}
