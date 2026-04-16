using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Commands.Create;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Handlers;

public sealed class CreateHospitalizationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizationsRead = new();
    private readonly Mock<IRepository<Hospitalization>> _hospitalizationsWrite = new();

    private CreateHospitalizationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _hospitalizationsRead.Object,
            _hospitalizationsWrite.Object);

    private static CreateHospitalizationCommand Cmd(
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null,
        DateTime? admittedAt = null,
        DateTime? plannedDischarge = null)
        => new(
            clinicId,
            petId,
            examinationId,
            admittedAt ?? DateTime.UtcNow.AddHours(-1),
            plannedDischarge,
            "Yatış nedeni",
            null);

    private void SetupTenantClinicPetSafe(Guid tid, Guid cid, Guid petId)
    {
        var pet = new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);

        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AdmittedAtTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(cid, petId, admittedAt: DateTime.UtcNow.AddDays(-10)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AdmittedAtTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PlannedDischargeBeforeAdmission()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var admitted = DateTime.UtcNow.AddHours(-1);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(cid, petId, admittedAt: admitted, plannedDischarge: admitted.AddHours(-2)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.PlannedDischargeBeforeAdmission");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ActiveHospitalizationExists()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(Cmd(cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ActiveHospitalizationExists");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationPetMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateHandler().Handle(Cmd(cid, petId, examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_Persist_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Hospitalization? added = null;
        _hospitalizationsWrite.Setup(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .Callback<Hospitalization, CancellationToken>((p, _) => added = p)
            .ReturnsAsync((Hospitalization p, CancellationToken _) => p);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(Cmd(cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        _hospitalizationsWrite.Verify(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
