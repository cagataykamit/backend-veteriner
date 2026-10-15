using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Specs;

/// <summary>Aşı listesi: birleşik tarih aralığı, müşteri (hayvan kümesi) süzgeci ve sıralama beyaz listesi.</summary>
public sealed class VaccinationsListDateClientSortSpecTests
{
    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private Vaccination Build(VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc, Guid? petId = null)
        => new(_tenantId, petId ?? Guid.NewGuid(), _clinicId, null, Guid.NewGuid(), "Kuduz", status, appliedAtUtc, dueAtUtc, null);

    private VaccinationsFilteredPagedSpec Paged(
        IReadOnlyCollection<Guid>? clientPetIds = null,
        DateTime? dateFromUtc = null,
        DateTime? dateToUtc = null,
        ListSortChoice<VaccinationListSortField>? sort = null)
        => new(
            _tenantId, _clinicId, null, null, null, null, null, null, 1, 50, null, [],
            clientPetIds: clientPetIds, dateFromUtc: dateFromUtc, dateToUtc: dateToUtc, sort: sort);

    private VaccinationsFilteredCountSpec Count(
        IReadOnlyCollection<Guid>? clientPetIds = null,
        DateTime? dateFromUtc = null,
        DateTime? dateToUtc = null)
        => new(
            _tenantId, _clinicId, null, null, null, null, null, null, null, [],
            clientPetIds: clientPetIds, dateFromUtc: dateFromUtc, dateToUtc: dateToUtc);

    [Fact]
    public void DateRange_Should_Use_AppliedAt_For_Applied_And_DueAt_Otherwise_Inclusive()
    {
        var appliedAtFromEdge = Build(VaccinationStatus.Applied, From, To.AddDays(30));
        var appliedBeforeButDueInside = Build(VaccinationStatus.Applied, From.AddDays(-1), From.AddDays(5));
        var scheduledAtToEdge = Build(VaccinationStatus.Scheduled, null, To);
        var scheduledJustAfter = Build(VaccinationStatus.Scheduled, null, To.AddSeconds(1));
        var cancelledDueInside = Build(VaccinationStatus.Cancelled, null, From.AddDays(10));
        var cancelledNoDue = Build(VaccinationStatus.Cancelled, null, null);
        var all = new[] { appliedAtFromEdge, appliedBeforeButDueInside, scheduledAtToEdge, scheduledJustAfter, cancelledDueInside, cancelledNoDue };
        var expected = new[] { appliedAtFromEdge.Id, scheduledAtToEdge.Id, cancelledDueInside.Id };

        Count(dateFromUtc: From, dateToUtc: To).Evaluate(all).Select(v => v.Id).Should().BeEquivalentTo(expected);
        Paged(dateFromUtc: From, dateToUtc: To).Evaluate(all).Select(v => v.Id).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void DateRange_Should_Allow_Open_Ended_Bounds()
    {
        var early = Build(VaccinationStatus.Scheduled, null, From.AddDays(-1));
        var late = Build(VaccinationStatus.Applied, To.AddDays(1), null);
        var all = new[] { early, late };

        Count(dateFromUtc: From).Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(late.Id);
        Count(dateToUtc: To).Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(early.Id);
    }

    [Fact]
    public void ClientPetIds_Should_Keep_Only_Client_Pets()
    {
        var petA = Guid.NewGuid();
        var mine = Build(VaccinationStatus.Scheduled, null, From, petA);
        var other = Build(VaccinationStatus.Scheduled, null, From);
        var all = new[] { mine, other };

        Count(clientPetIds: [petA]).Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        Paged(clientPetIds: [petA]).Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        Count(clientPetIds: []).Evaluate(all).Should().BeEmpty();
        Count().Evaluate(all).Should().HaveCount(2);
    }

    [Fact]
    public void Default_Order_Should_Stay_Combined_Date_Descending()
    {
        var applied = Build(VaccinationStatus.Applied, From.AddDays(2), From.AddDays(40));
        var scheduled = Build(VaccinationStatus.Scheduled, null, From.AddDays(5));
        var oldest = Build(VaccinationStatus.Cancelled, null, From);

        Paged().Evaluate([applied, scheduled, oldest]).Select(v => v.Id)
            .Should().Equal(scheduled.Id, applied.Id, oldest.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sort_DueAtUtc_Should_Order_By_Due_With_Nulls_Last(bool descending)
    {
        var dueEarly = Build(VaccinationStatus.Scheduled, null, From);
        var dueLate = Build(VaccinationStatus.Scheduled, null, To);
        var noDue = Build(VaccinationStatus.Applied, From.AddDays(1), null);

        var ids = Paged(sort: new ListSortChoice<VaccinationListSortField>(VaccinationListSortField.DueAtUtc, descending))
            .Evaluate([noDue, dueLate, dueEarly]).Select(v => v.Id).ToList();

        var expected = descending
            ? new[] { dueLate.Id, dueEarly.Id, noDue.Id }
            : new[] { dueEarly.Id, dueLate.Id, noDue.Id };
        ids.Should().Equal(expected);
    }

    [Fact]
    public void Sort_AppliedAtUtc_Asc_Should_Order_By_Applied_With_Nulls_Last()
    {
        var notApplied = Build(VaccinationStatus.Scheduled, null, From);
        var appliedLate = Build(VaccinationStatus.Applied, To, null);
        var appliedEarly = Build(VaccinationStatus.Applied, From, null);

        Paged(sort: new ListSortChoice<VaccinationListSortField>(VaccinationListSortField.AppliedAtUtc, false))
            .Evaluate([notApplied, appliedLate, appliedEarly]).Select(v => v.Id)
            .Should().Equal(appliedEarly.Id, appliedLate.Id, notApplied.Id);
    }
}
