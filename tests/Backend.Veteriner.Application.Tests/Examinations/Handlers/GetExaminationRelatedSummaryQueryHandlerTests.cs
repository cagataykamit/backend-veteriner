using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Queries.GetRelatedSummary;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class GetExaminationRelatedSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptions = new();
    private readonly Mock<IReadRepository<LabResult>> _labResults = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetExaminationRelatedSummaryQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetExaminationRelatedSummaryQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _examinations.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _treatments.Object,
            _prescriptions.Object,
            _labResults.Object,
            _hospitalizations.Object,
            _payments.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Examination BuildExamination(Guid tenantId, Guid clinicId, Guid petId, Guid? examinationId = null)
    {
        var entity = new Examination(
            tenantId,
            clinicId,
            petId,
            null,
            DateTime.UtcNow.AddHours(-1),
            "Neden",
            "Bulgu",
            null,
            null);
        if (examinationId.HasValue)
            typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(entity, examinationId.Value);
        return entity;
    }

    private void SetupExaminationFound(Examination entity)
        => _examinations.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

    private void SetupTenantWideRole(params string[] claimNames)
        => _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimNames);

    private void SetupAssignment(Guid clinicId, bool assigned)
        => _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

    private void SetupSuccessGraph(Guid tenantId, Guid clinicId, Guid petId, Guid examinationId)
    {
        var pet = new Pet(tenantId, Guid.NewGuid(), "Minnoş", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);

        var treatment = new Treatment(
            tenantId,
            clinicId,
            petId,
            examinationId,
            DateTime.UtcNow.AddHours(-1),
            "Tedavi",
            "Açıklama",
            null,
            null);
        var clinic = new Clinic(tenantId, "Klinik", "Şehir");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Client>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Treatment> { treatment });
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        _labResults.Setup(r => r.ListAsync(It.IsAny<LabResultsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LabResult>());
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization>());
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { clinic });
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_ExaminationMissing()
    {
        SetupTenant(Guid.NewGuid());
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<ISpecification<Treatment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToExaminationClinic()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupAssignment(clinicId, assigned: false);
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<TreatmentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _prescriptions.Verify(
            r => r.ListAsync(It.IsAny<PrescriptionsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _labResults.Verify(
            r => r.ListAsync(It.IsAny<LabResultsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_ActiveClinicContextDiffersFromExaminationClinic()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<TreatmentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnSummary_When_TenantWideUserReadsWithoutActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExaminationId.Should().Be(examId);
        result.Value.Treatments.Should().ContainSingle().Which.Title.Should().Be("Tedavi");
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnSummary_When_AssignedNonTenantWideUserReadsOwnClinicExamination()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupAssignment(clinicId, assigned: true);
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PetName.Should().Be("Minnoş");
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_UserContextMissing()
    {
        SetupTenant(Guid.NewGuid());
        _clientContext.SetupGet(x => x.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
        _treatments.Verify(
            r => r.ListAsync(It.IsAny<ISpecification<Treatment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupExaminationFound(BuildExamination(tenantId, clinicId, petId, examId));
        SetupSuccessGraph(tenantId, clinicId, petId, examId);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), token);

        _examinations.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
        _treatments.Verify(x => x.ListAsync(It.IsAny<TreatmentsForExaminationRelatedSpec>(), token), Times.Once);
    }
}
