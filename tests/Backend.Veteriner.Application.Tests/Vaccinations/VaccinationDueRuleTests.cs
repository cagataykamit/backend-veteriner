using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations;

/// <summary>
/// ADR-028: gecikmiş = vade son 1 takvim ayı içinde ve bugünün (İstanbul) başından önce; yaklaşan 7 gün = <c>[bugünün başı, bugün + 7 gün başı)</c>.
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
        => new(_tenantId, petId ?? Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Karma", VaccinationStatus.Scheduled, null, dueUtc, null, DateTime.UtcNow);

    [Fact]
    public void Boundaries_Should_Be_Istanbul_Days()
    {
        VaccinationDueRule.OverdueWindow(NowUtc).BeforeUtc.Should().Be(TodayStartUtc);
        VaccinationDueRule.UpcomingWindow(NowUtc, 7).Should().Be((TodayStartUtc, TodayStartUtc.AddDays(7)));
    }

    [Fact]
    public void Boundary_Should_Move_At_Istanbul_Midnight_Not_Utc_Midnight()
    {
        // 2026-10-15 21:30 UTC = İstanbul 2026-10-16 00:30: UTC günü hâlâ 15'i ama İstanbul günü 16'sı.
        VaccinationDueRule.OverdueWindow(new DateTime(2026, 10, 15, 21, 30, 0, DateTimeKind.Utc)).BeforeUtc.Should().Be(TodayStartUtc);
        // 2026-10-15 20:59 UTC = İstanbul 2026-10-15 23:59: önceki gün.
        VaccinationDueRule.OverdueWindow(new DateTime(2026, 10, 15, 20, 59, 0, DateTimeKind.Utc)).BeforeUtc.Should().Be(TodayStartUtc.AddDays(-1));
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
        var overdueWindow = VaccinationDueRule.OverdueWindow(NowUtc);

        VaccinationDueRule.IsOverdueAt(due, overdueWindow).Should().Be(expectedOverdue);
        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueWindow)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
        new VaccinationsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
        new VaccinationsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, null, null, null, 1, 20, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
    }

    [Fact]
    public void Overdue_Window_Should_Start_One_Calendar_Month_Before_Today()
    {
        // Bugün İstanbul 2026-10-16; pencere başı İstanbul 2026-09-16 00:00 = 2026-09-15 21:00 UTC.
        VaccinationDueRule.OverdueVisibleMonths.Should().Be(1);
        VaccinationDueRule.OverdueWindow(NowUtc)
            .Should().Be(new VaccinationOverdueWindow(new DateTime(2026, 9, 15, 21, 0, 0, DateTimeKind.Utc), TodayStartUtc));
        VaccinationDueRule.OverdueWindow(new DateOnly(2026, 10, 16)).Should().Be(VaccinationDueRule.OverdueWindow(NowUtc));
    }

    /// <summary>
    /// Kullanıcı kararı 2026-11-21: vade günü D, bugün T için <c>T.AddMonths(-1) ≤ D &lt; T</c> ise gecikmiş; daha eskisi hiçbir gecikmiş
    /// görünümüne girmez. Takvim ayı (sabit gün sayısı değil); ay sonu kırpılır.
    /// </summary>
    [Theory]
    [InlineData("2026-10-16", "2026-09-16 00:00", true)] // tam 1 ay önce, gün başı
    [InlineData("2026-10-16", "2026-09-16 23:59", true)]
    [InlineData("2026-10-16", "2026-09-15 23:59", false)] // bir gün daha eski
    [InlineData("2026-10-16", "2026-10-15 23:59", true)] // dün
    [InlineData("2026-10-16", "2026-10-16 00:00", false)] // bugün
    [InlineData("2026-03-31", "2026-02-28 00:00", true)] // 31 Mart → 28 Şubat (ay sonu kırpılır)
    [InlineData("2026-03-31", "2026-02-27 23:59", false)]
    [InlineData("2028-03-31", "2028-02-29 00:00", true)] // artık yıl: 31 Mart → 29 Şubat
    [InlineData("2028-03-31", "2028-02-28 23:59", false)]
    [InlineData("2026-03-01", "2026-02-01 00:00", true)] // 1 Mart → 1 Şubat (28 gün; 31 gün değil)
    [InlineData("2026-03-01", "2026-01-31 23:59", false)]
    [InlineData("2026-01-15", "2025-12-15 00:00", true)] // yıl dönümü
    [InlineData("2026-01-15", "2025-12-14 23:59", false)]
    [InlineData("2026-12-31", "2026-11-30 00:00", true)] // 31 Aralık → 30 Kasım
    [InlineData("2026-12-31", "2026-11-29 23:59", false)]
    public void Overdue_Window_Boundaries_Should_Use_Calendar_Months(string today, string dueLocal, bool expectedOverdue)
    {
        var todayDate = DateOnly.ParseExact(today, "yyyy-MM-dd");
        var nowUtc = OperationDayBounds.NoonUtcForLocalDate(todayDate);
        var due = OperationDayBounds.FromLocal(DateTime.ParseExact(dueLocal, "yyyy-MM-dd HH:mm", null));
        var window = VaccinationDueRule.OverdueWindow(nowUtc);

        VaccinationDueRule.IsOverdueAt(due, window).Should().Be(expectedOverdue);
        VaccinationDueRule.IsBeforeOverdueWindow(due, window).Should().Be(!expectedOverdue && due < window.BeforeUtc);
        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, window)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
        new VaccinationsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: nowUtc)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
        new VaccinationsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, null, null, null, 1, 20, null, [], onlyOverdue: true, listAsOfUtc: nowUtc)
            .Evaluate([Scheduled(due)]).Should().HaveCount(expectedOverdue ? 1 : 0);
    }

    [Fact]
    public void Scheduled_Status_Filter_Should_Still_List_Vaccinations_Older_Than_Window()
    {
        // Pencereden eski planlı aşı gecikmiş listesinden düşer ama aşı listesinde durumuyla (Planlandı) kalır.
        var old = Scheduled(TodayStartUtc.AddMonths(-3));

        new VaccinationsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc)
            .Evaluate([old]).Should().BeEmpty();
        new VaccinationsFilteredCountSpec(_tenantId, _clinicId, null, VaccinationStatus.Scheduled, null, null, null, null, null, [])
            .Evaluate([old]).Should().ContainSingle();
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
        var overdueWindow = VaccinationDueRule.OverdueWindow(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, 7);
        var cancelledPast = new Vaccination(_tenantId, Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Karma", VaccinationStatus.Cancelled, null, TodayStartUtc.AddDays(-2), null, DateTime.UtcNow);
        var cancelledSoon = new Vaccination(_tenantId, Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Karma", VaccinationStatus.Cancelled, null, TodayStartUtc.AddDays(2), null, DateTime.UtcNow);

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueWindow).Evaluate([cancelledPast]).Should().BeEmpty();
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to).Evaluate([cancelledSoon]).Should().BeEmpty();
    }

    [Fact]
    public void Deceased_Pets_Are_Excluded_From_Both_Counts()
    {
        var deceasedPetId = Guid.NewGuid();
        var overdueWindow = VaccinationDueRule.OverdueWindow(NowUtc);
        var (from, to) = VaccinationDueRule.UpcomingWindow(NowUtc, 7);
        Vaccination[] rows =
        [
            Scheduled(TodayStartUtc.AddDays(-1), deceasedPetId),
            Scheduled(TodayStartUtc.AddDays(-1)),
            Scheduled(TodayStartUtc.AddDays(2), deceasedPetId),
            Scheduled(TodayStartUtc.AddDays(2)),
        ];

        new DashboardOverdueVaccinationsCountSpec(_tenantId, _clinicId, overdueWindow, null, [deceasedPetId])
            .Evaluate(rows).Should().ContainSingle(v => v.PetId != deceasedPetId);
        new DashboardUpcomingVaccinationsNext7DaysCountSpec(_tenantId, _clinicId, from, to, null, [deceasedPetId])
            .Evaluate(rows).Should().ContainSingle(v => v.PetId != deceasedPetId);
        new VaccinationsFilteredCountSpec(
                _tenantId, _clinicId, null, null, null, null, null, null, null, [], onlyOverdue: true, listAsOfUtc: NowUtc,
                notReceivingCarePetIds: [deceasedPetId])
            .Evaluate(rows).Should().ContainSingle(v => v.PetId != deceasedPetId);
    }
}
