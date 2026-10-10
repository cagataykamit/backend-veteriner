using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Queries.GetList.Validators;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Examinations;

public sealed class ExaminationsListVisitFilterTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private Examination NewExamination(Guid? visitId, Guid? clinicId = null, Guid? tenantId = null)
        => new(
            tenantId ?? _tenantId, clinicId ?? _clinicId, Guid.NewGuid(), appointmentId: null,
            DateTime.UtcNow.AddHours(-1), "Kontrol", "Bulgu", null, null, visitId: visitId);

    private static GetExaminationsListQueryValidator Validator => new();

    [Fact]
    public void Validator_Should_Reject_Empty_VisitId()
    {
        var result = Validator.Validate(new GetExaminationsListQuery(new PageRequest { Page = 1, PageSize = 20 }, VisitId: Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "visitId is invalid.");
    }

    [Fact]
    public void Validator_Should_Accept_Missing_And_Valid_VisitId()
    {
        var page = new PageRequest { Page = 1, PageSize = 20 };

        Validator.Validate(new GetExaminationsListQuery(page)).IsValid.Should().BeTrue();
        Validator.Validate(new GetExaminationsListQuery(page, VisitId: Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Specs_Should_Filter_By_VisitId_Within_Tenant_And_Accessible_Clinics()
    {
        var visitId = Guid.NewGuid();
        var match = NewExamination(visitId);
        var otherVisit = NewExamination(Guid.NewGuid());
        var noVisit = NewExamination(null);
        var otherTenant = NewExamination(visitId, tenantId: Guid.NewGuid());
        var otherClinic = NewExamination(visitId, clinicId: Guid.NewGuid());
        var all = new[] { match, otherVisit, noVisit, otherTenant, otherClinic };

        var count = new ExaminationsFilteredCountSpec(
            _tenantId, null, null, null, null, null, null, [], new[] { _clinicId }, visitId);
        var paged = new ExaminationsFilteredPagedSpec(
            _tenantId, null, null, null, null, null, 1, 20, null, [], new[] { _clinicId }, visitId);

        count.Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(match.Id);
        paged.Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    [Fact]
    public void Specs_Without_VisitId_Should_Not_Filter_By_Visit()
    {
        var all = new[] { NewExamination(Guid.NewGuid()), NewExamination(null) };

        new ExaminationsFilteredCountSpec(_tenantId, null, null, null, null, null, null, [])
            .Evaluate(all).Should().HaveCount(2);
    }
}
