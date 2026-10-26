using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Treatments;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Treatments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Treatments;

/// <summary>ADR-025 madde 2: aktif tedavi = takip tarihi İstanbul günüyle bugün veya sonrası; takipsiz tedavi aktif değil.</summary>
public sealed class ActiveTreatmentRuleTests
{
    // 2026-10-15 21:30 UTC = İstanbul 2026-10-16 00:30; "bugün" İstanbul günüdür, sınır 2026-10-15 21:00 UTC.
    private static readonly DateTime NowUtc = new(2026, 10, 15, 21, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime IstanbulTodayStartUtc = new(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private Treatment New(DateTime? followUpUtc)
        => new(_tenantId, _clinicId, Guid.NewGuid(), null, NowUtc.AddDays(-2), "Tedavi", "Açıklama", null, followUpUtc);

    [Fact]
    public void FollowUpFromUtc_Should_Be_Istanbul_Day_Start()
    {
        ActiveTreatmentRule.FollowUpFromUtc(NowUtc).Should().Be(IstanbulTodayStartUtc);
    }

    [Fact]
    public void Specs_Should_Keep_Only_Today_Or_Later_FollowUps_Nearest_First()
    {
        var noFollowUp = New(null);
        var yesterdayLate = New(IstanbulTodayStartUtc.AddMinutes(-1));
        var todayStart = New(IstanbulTodayStartUtc);
        var nextWeek = New(IstanbulTodayStartUtc.AddDays(7));
        var tomorrow = New(IstanbulTodayStartUtc.AddDays(1));
        Treatment[] all = [noFollowUp, yesterdayLate, nextWeek, todayStart, tomorrow];
        var from = ActiveTreatmentRule.FollowUpFromUtc(NowUtc);

        var paged = new TreatmentsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, 1, 20, null, [], null, null, from);
        var count = new TreatmentsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, [], null, from);

        paged.Evaluate(all).Select(r => r.Id).Should().Equal(todayStart.Id, tomorrow.Id, nextWeek.Id);
        count.Evaluate(all).Should().HaveCount(3);
    }

    [Fact]
    public void Paged_Spec_Should_Honor_Explicit_Sort_With_Active_Filter()
    {
        var a = new Treatment(_tenantId, _clinicId, Guid.NewGuid(), null, NowUtc.AddDays(-3), "A", "x", null, IstanbulTodayStartUtc.AddDays(1));
        var b = new Treatment(_tenantId, _clinicId, Guid.NewGuid(), null, NowUtc.AddDays(-1), "B", "x", null, IstanbulTodayStartUtc.AddDays(5));
        var sort = new ListSortChoice<TreatmentListSortField>(TreatmentListSortField.TreatmentDateUtc, true);

        var spec = new TreatmentsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, 1, 20, null, [], null, sort, IstanbulTodayStartUtc);

        spec.Evaluate([a, b]).Select(r => r.Id).Should().Equal(b.Id, a.Id);
    }

    [Fact]
    public void Without_Active_Filter_All_Treatments_Are_Listed()
    {
        Treatment[] all = [New(null), New(IstanbulTodayStartUtc.AddDays(-3)), New(IstanbulTodayStartUtc.AddDays(3))];

        new TreatmentsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, []).Evaluate(all).Should().HaveCount(3);
    }
}
