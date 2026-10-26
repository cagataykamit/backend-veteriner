using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Queries.GetRelatedSummary;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
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
    private readonly Mock<IReadRepository<Administration>> _administrations = new();
    private readonly Mock<IReadRepository<Product>> _products = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<ImagingRecord>> _imaging = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.ChargeSuggestions.ChargeSuggestion>> _chargeSuggestions = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetExaminationRelatedSummaryQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _permissions.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);
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
            _administrations.Object,
            _products.Object,
            _vaccinations.Object,
            _imaging.Object,
            _payments.Object,
            _chargeSuggestions.Object,
            _permissions.Object);

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
        _administrations.Setup(r => r.ListAsync(It.IsAny<AdministrationsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Administration>());
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination>());
        _imaging.Setup(r => r.ListAsync(It.IsAny<ImagingForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImagingRecord>());
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
    public async Task Handle_Should_Include_Administrations_With_Late_Entry_Marker()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);

        var product = new Product(tid, "Amoksisilin", "şişe", 10m, "TRY", sku: "SKU-1");
        var late = Administration.Create(
            tid, clinicId, petId, examId, null, product.Id, 1.5m, DateTime.UtcNow, null,
            true, "Muayene sonrası uygulandı", Guid.NewGuid(), DateTime.UtcNow, null).Value!;
        var normal = Administration.Create(
            tid, clinicId, petId, examId, null, product.Id, 1m, DateTime.UtcNow, null,
            false, null, Guid.NewGuid(), DateTime.UtcNow, null).Value!;
        _administrations.Setup(r => r.ListAsync(It.IsAny<AdministrationsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Administration> { late, normal });
        _products.Setup(r => r.ListAsync(It.IsAny<ProductsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Product> { product });

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value!.Administrations;
        items.Should().HaveCount(2);
        var lateItem = items.Single(i => i.Id == late.Id);
        lateItem.ProductName.Should().Be("Amoksisilin");
        lateItem.Unit.Should().Be("şişe");
        lateItem.Quantity.Should().Be(1.5m);
        lateItem.AddedAfterFinalization.Should().BeTrue();
        lateItem.LateEntryReason.Should().Be("Muayene sonrası uygulandı");
        items.Single(i => i.Id == normal.Id).AddedAfterFinalization.Should().BeFalse();
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
    public async Task Handle_Should_OmitRecordKinds_When_UserLacksTheirReadPermission()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupAssignment(clinicId, assigned: true);
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Treatments.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Prescriptions.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.LabResults.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Hospitalizations.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Payments.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Administrations.Read)).Returns(false);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        _administrations.Verify(r => r.ListAsync(It.IsAny<ISpecification<Administration>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Treatments.Should().BeEmpty();
        result.Value.Prescriptions.Should().BeEmpty();
        result.Value.LabResults.Should().BeEmpty();
        result.Value.Hospitalizations.Should().BeEmpty();
        result.Value.Payments.Should().BeEmpty();
        _treatments.Verify(r => r.ListAsync(It.IsAny<TreatmentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _prescriptions.Verify(r => r.ListAsync(It.IsAny<PrescriptionsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _labResults.Verify(r => r.ListAsync(It.IsAny<LabResultsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _hospitalizations.Verify(r => r.ListAsync(It.IsAny<HospitalizationsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _payments.Verify(r => r.ListAsync(It.IsAny<PaymentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Include_Vaccinations_Linked_To_Examination()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);
        var applied = new Vaccination(
            tid, petId, clinicId, examId, Guid.NewGuid(), "Kuduz",
            VaccinationStatus.Applied, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddYears(1), null);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Vaccination> { applied });

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Vaccinations.Should().ContainSingle().Subject;
        item.Id.Should().Be(applied.Id);
        item.VaccineName.Should().Be("Kuduz");
        item.Status.Should().Be(VaccinationStatus.Applied);
        item.ClinicName.Should().Be("Klinik");
        item.AppliedAtUtc.Should().Be(applied.AppliedAtUtc);
        item.DueAtUtc.Should().Be(applied.DueAtUtc);
    }

    [Fact]
    public async Task Handle_Should_Include_Imaging_Linked_To_Examination_With_Cancelled_And_Badge()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);
        var now = DateTime.UtcNow;
        var added = ImagingRecord.Create(tid, clinicId, petId, examId, true, now.AddHours(-1), ImagingModality.XRay, "Sol arka bacak", null, Guid.NewGuid(), now).Value!;
        var cancelled = ImagingRecord.Create(tid, clinicId, petId, examId, false, now.AddHours(-2), ImagingModality.Ultrasound, null, null, Guid.NewGuid(), now).Value!;
        cancelled.Cancel(Guid.NewGuid(), "Yanlis hasta secildi", now);
        _imaging.Setup(r => r.ListAsync(It.IsAny<ImagingForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImagingRecord> { added, cancelled });

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Imaging.Should().HaveCount(2);
        var first = result.Value.Imaging[0];
        first.Id.Should().Be(added.Id);
        first.Modality.Should().Be(ImagingModality.XRay);
        first.Description.Should().Be("Sol arka bacak");
        first.Status.Should().Be(ImagingRecordStatus.Active);
        first.AddedAfterFinalization.Should().BeTrue();
        first.ClinicName.Should().Be("Klinik");
        result.Value.Imaging[1].Status.Should().Be(ImagingRecordStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_Should_OmitImaging_When_UserLacksImagingReadPermission()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Imaging.Read)).Returns(false);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Imaging.Should().BeEmpty();
        result.Value.Treatments.Should().ContainSingle();
        _imaging.Verify(r => r.ListAsync(It.IsAny<ISpecification<ImagingRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_OmitVaccinations_When_UserLacksVaccinationsReadPermission()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("Admin");
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Vaccinations.Read)).Returns(false);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Vaccinations.Should().BeEmpty();
        result.Value.Treatments.Should().ContainSingle();
        _vaccinations.Verify(r => r.ListAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnOnlyPermittedKinds_When_UserHasPartialPermissions()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid);
        SetupAssignment(clinicId, assigned: true);
        SetupExaminationFound(BuildExamination(tid, clinicId, petId, examId));
        SetupSuccessGraph(tid, clinicId, petId, examId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Payments.Read)).Returns(false);

        var result = await CreateHandler().Handle(new GetExaminationRelatedSummaryQuery(examId), CancellationToken.None);

        result.Value!.Treatments.Should().ContainSingle();
        _payments.Verify(r => r.ListAsync(It.IsAny<PaymentsForExaminationRelatedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
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
