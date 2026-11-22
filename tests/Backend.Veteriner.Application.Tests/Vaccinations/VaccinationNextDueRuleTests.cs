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
        new AppliedVaccinationDosesForPetsSpec(_tenantId, [_petId], source.AppliedAtUtc!.Value)
            .Evaluate([source, laterInOtherClinic]).Should().ContainSingle().Which.Id.Should().Be(laterInOtherClinic.Id);
    }

    [Fact]
    public void Cancelled_Later_Dose_Should_Not_Fulfil()
    {
        var source = Applied(TodayStartUtc.AddYears(-1), TodayStartUtc.AddDays(-3));
        var cancelled = Applied(TodayStartUtc.AddDays(-1), null);
        cancelled.Cancel(Guid.NewGuid(), "Yanlış hastaya girildi.", NowUtc).IsSuccess.Should().BeTrue();

        new AppliedVaccinationDosesForPetsSpec(_tenantId, [_petId], source.AppliedAtUtc!.Value).Evaluate([cancelled]).Should().BeEmpty();
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
        AppliedVaccinationDosesForPetsSpec? laterSpec = null;
        repo.Setup(r => r.ListAsync(It.IsAny<AppliedVaccinationDosesForPetsSpec>(), It.IsAny<CancellationToken>()))
            .Callback<ISpecification<Vaccination, VaccinationDose>, CancellationToken>((s, _) => laterSpec = (AppliedVaccinationDosesForPetsSpec)s)
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
        repo.Verify(r => r.ListAsync(It.IsAny<AppliedVaccinationDosesForPetsSpec>(), It.IsAny<CancellationToken>()), Times.Never);
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
