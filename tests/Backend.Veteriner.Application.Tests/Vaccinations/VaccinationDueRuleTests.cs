using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations;

/// <summary>
/// ADR-028: gecikmiş = vade bugünün (İstanbul) başından önce; yaklaşan 7 gün = <c>[bugünün başı, bugün + 7 gün başı)</c>.
/// Pano sayaçları ve <c>onlyOverdue</c> liste süzgeci aynı kuralı kullanır; vefat edenler dışlanır.
/// </summary>
public sealed class VaccinationDueRuleTests
{
    // 2026-10-16 15:00 UTC = İstanbul 18:00; bugünün başı İstanbul 2026-10-16 00:00 = 2026-10-15 21:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayStartUtc = new(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private Vaccination Scheduled(DateTime dueUtc, Guid? petId = null)
        => new(_tenantId, petId ?? Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Karma", VaccinationStatus.Scheduled, null, dueUtc, null);

    [Fact]
    public void Boundaries_Should_Be_Istanbul_Days()
    {
        VaccinationDueRule.OverdueBeforeUtc(NowUtc).Should().Be(TodayStartUtc);
        VaccinationDueRule.UpcomingWindow(NowUtc, 7).Should().Be((TodayStartUtc, TodayStartUtc.AddDays(7)));
    }

    [Fact]
    public void Boundary_Should_Move_At_Istanbul_Midnight_Not_Utc_Midnight()
    {
        // 2026-10-15 21:30 UTC = İstanbul 2026-10-16 00:30: UTC günü hâlâ 15'i ama İstanbul günü 16'sı.
        VaccinationDueRule.OverdueBeforeUtc(new DateTime(2026, 10, 15, 21, 30, 0, DateTimeKind.Utc)).Should().Be(TodayStartUtc);
        // 2026-10-15 20:59 UTC = İstanbul 2026-10-15 23:59: önceki gün.
        VaccinationDueRule.OverdueBeforeUtc(new DateTime(2026, 10, 15, 20, 59, 0, DateTimeKind.Utc)).Should().Be(TodayStartUtc.AddDays(-1));
    }

    [Theory]
    [InlineData(0, false)] // bugün 00:00
    [InlineData(9 * 60, false)] // bugün 09:00, saati geçmiş (şimdi 18:00) — gecikmiş değil
    [InlineData(23 * 60 + 59, false)] // bugün 23:59
    [InlineData(-1, true)] // dün 23:59
    [InlineData(-3 * 24 * 60, true)]
    public void Overdue_Should_Be_Before_Today_Start(int minutesFromTodayStart, bool expectedOverdue)
    {
        var due = TodayStartUtc.AddMinutes(minutesFromTodayStart);
        var overdueBefore = VaccinationDueRule.OverdueBeforeUtc(NowUtc);

        VaccinationDueRule.IsOverdueAt(due, overdueBefore).Should().Be(expectedOverdue);
        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueBefore)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
        new VaccinationsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
        new VaccinationsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, null, null, null, 1, 20, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
    }

    [Theory]
    [InlineData(0, true)] // bugün 00:00
    [InlineData(9 * 60, true)] // bugün saati geçmiş
    [InlineData(23 * 60 + 59, true)] // bugün 23:59
    [InlineData(-1, false)] // dün 23:59 (gecikmiş)
    [InlineData(7 * 24 * 60 - 1, true)] // +6 gün 23:59
    [InlineData(7 * 24 * 60, false)] // +7 gün 00:00
    public void Upcoming_7_Days_Should_Include_Today_And_Six_More_Days(int minutesFromTodayStart, bool expectedUpcoming)
    {
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, VaccinationDueRule.DashboardUpcomingDays);

        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to)
            .Evaluate([Scheduled(TodayStartUtc.AddMinutes(minutesFromTodayStart))])
            .Should().HaveCount(expectedUpcoming ? 1 : 0);
    }

    [Fact]
    public void Dashboard_Upcoming_Count_Should_Match_List_Link_Range()
    {
        // Pano bağlantısı: status=0, fromDate=bugün, toDate=bugün+6 (İstanbul), onlyReceivingCare; frontend günü 00:00 ve 23:59:59.999 gönderir.
        var deceasedPetId = Guid.NewGuid();
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, VaccinationDueRule.DashboardUpcomingDays);
        Vaccination[] rows =
        [
            Scheduled(TodayStartUtc.AddMinutes(-1)),
            Scheduled(TodayStartUtc),
            Scheduled(TodayStartUtc.AddHours(9)),
            Scheduled(TodayStartUtc.AddDays(7).AddMinutes(-1)),
            Scheduled(TodayStartUtc.AddDays(7)),
            Scheduled(TodayStartUtc.AddDays(3), deceasedPetId),
        ];

        var dashboard = new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to, null, [deceasedPetId]).Evaluate(rows);
        var list = new VaccinationsFilteredCountSpec(
                _tenantId, _clinicId, null, VaccinationStatus.Scheduled, null, null, null, null, null, [],
                notReceivingCarePetIds: [deceasedPetId], dateFromUtc: from, dateToUtc: to.AddMilliseconds(-1))
            .Evaluate(rows);

        dashboard.Should().HaveCount(3);
        list.Select(v => v.Id).Should().BeEquivalentTo(dashboard.Select(v => v.Id));
    }

    [Fact]
    public void Only_Scheduled_Vaccinations_Count()
    {
        var overdueBefore = VaccinationDueRule.OverdueBeforeUtc(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, 7);
        var cancelledPast = new Vaccination(_tenantId, Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Karma", VaccinationStatus.Cancelled, null, TodayStartUtc.AddDays(-2), null);
        var cancelledSoon = new Vaccination(_tenantId, Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Karma", VaccinationStatus.Cancelled, null, TodayStartUtc.AddDays(2), null);

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueBefore).Evaluate([cancelledPast]).Should().BeEmpty();
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to).Evaluate([cancelledSoon]).Should().BeEmpty();
    }

    [Fact]
    public void Deceased_Pets_Are_Excluded_From_Both_Counts()
    {
        var deceasedPetId = Guid.NewGuid();
        var overdueBefore = VaccinationDueRule.OverdueBeforeUtc(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, 7);
        Vaccination[] rows =
        [
            Scheduled(TodayStartUtc.AddDays(-1), deceasedPetId),
            Scheduled(TodayStartUtc.AddDays(-1)),
            Scheduled(TodayStartUtc.AddDays(2), deceasedPetId),
            Scheduled(TodayStartUtc.AddDays(2)),
        ];

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueBefore, null, [deceasedPetId])
            .Evaluate(rows).Should().ContainSingle(v => v.PetId != deceasedPetId);
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to, null, [deceasedPetId])
            .Evaluate(rows).Should().ContainSingle(v => v.PetId != deceasedPetId);
        new VaccinationsFilteredCountSpec(
                _tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc,
                notReceivingCarePetIds: [deceasedPetId])
            .Evaluate(rows).Should().ContainSingle(v => v.PetId != deceasedPetId);
    }
}
