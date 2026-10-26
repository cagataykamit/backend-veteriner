using Ardalis.Specification;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Specs;

public sealed class VaccinationsExaminationFilterSpecTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    private Vaccination Build(Guid tenantId, Guid? examinationId, Guid? clinicId = null)
        => new(tenantId, _petId, clinicId ?? _clinicId, examinationId, Guid.NewGuid(), "Kuduz",
            VaccinationStatus.Applied, DateTime.UtcNow.AddHours(-1), null, null);

    [Fact]
    public void ListSpec_Should_FilterByExaminationId_Within_TenantAndClinic()
    {
        var examId = Guid.NewGuid();
        var match = Build(_tenantId, examId);
        var otherExam = Build(_tenantId, Guid.NewGuid());
        var noExam = Build(_tenantId, null);
        var otherTenant = Build(Guid.NewGuid(), examId);
        var otherClinic = Build(_tenantId, examId, Guid.NewGuid());
        var all = new[] { match, otherExam, noExam, otherTenant, otherClinic };

        var spec = new VaccinationsFilteredPagedSpec(
            _tenantId, _clinicId, null, null, null, null, null, null, 1, 50, null, [], null, false, null, examId);

        spec.Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    [Fact]
    public void CountSpec_Should_FilterByExaminationId()
    {
        var examId = Guid.NewGuid();
        var all = new[] { Build(_tenantId, examId), Build(_tenantId, examId), Build(_tenantId, Guid.NewGuid()) };

        var spec = new VaccinationsFilteredCountSpec(
            _tenantId, _clinicId, null, null, null, null, null, null, null, [], null, false, null, examId);

        spec.Evaluate(all).Should().HaveCount(2);
    }

    [Fact]
    public void ListSpec_Should_NotFilterByExamination_When_NotProvided()
    {
        var all = new[] { Build(_tenantId, Guid.NewGuid()), Build(_tenantId, null) };

        var spec = new VaccinationsFilteredPagedSpec(
            _tenantId, _clinicId, null, null, null, null, null, null, 1, 50, null, [], null);

        spec.Evaluate(all).Should().HaveCount(2);
    }
}
