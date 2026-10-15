using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Examinations;

public sealed class ExaminationsListClientSortSpecTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private Examination NewExamination(DateTime examinedAtUtc, Guid? petId = null)
        => new(_tenantId, _clinicId, petId ?? Guid.NewGuid(), appointmentId: null, examinedAtUtc, "Kontrol", "Bulgu", null, null);

    private ExaminationsFilteredPagedSpec Paged(
        IReadOnlyCollection<Guid>? clientPetIds = null,
        ListSortChoice<ExaminationListSortField>? sort = null)
        => new(_tenantId, _clinicId, null, null, null, null, 1, 20, null, [], clientPetIds: clientPetIds, sort: sort);

    [Fact]
    public void ClientPetIds_Should_Filter_Count_And_Page()
    {
        var petA = Guid.NewGuid();
        var mine = NewExamination(DateTime.UtcNow.AddHours(-2), petA);
        var other = NewExamination(DateTime.UtcNow.AddHours(-1));
        var all = new[] { mine, other };

        new ExaminationsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, [], clientPetIds: [petA])
            .Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        Paged(clientPetIds: [petA]).Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        Paged().Evaluate(all).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Sort_Should_Order_By_ExaminedAtUtc(bool? descending, bool expectNewestFirst)
    {
        var older = NewExamination(DateTime.UtcNow.AddHours(-3));
        var newer = NewExamination(DateTime.UtcNow.AddHours(-1));
        ListSortChoice<ExaminationListSortField>? sort = descending is { } d
            ? new ListSortChoice<ExaminationListSortField>(ExaminationListSortField.ExaminedAtUtc, d)
            : null;

        var ids = Paged(sort: sort).Evaluate([older, newer]).Select(r => r.Id).ToList();

        ids.Should().Equal(expectNewestFirst ? new[] { newer.Id, older.Id } : new[] { older.Id, newer.Id });
    }
}
