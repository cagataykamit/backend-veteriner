using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Treatments.Commands.Update;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Treatments.Handlers;

public sealed class UpdateTreatmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Treatment>> _treatmentsRead = new();
    private readonly Mock<IRepository<Treatment>> _treatmentsWrite = new();

    private UpdateTreatmentCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _treatmentsRead.Object,
            _treatmentsWrite.Object);

    private static UpdateTreatmentCommand Cmd(
        Guid id,
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null,
        DateTime? treatmentDate = null,
        DateTime? followUp = null)
        => new(
            id,
            clinicId,
            petId,
            examinationId,
            treatmentDate ?? DateTime.UtcNow.AddHours(-1),
            "Başlık",
            "Açıklama",
            null,
            followUp);

    private void SetupTenantClinicPet(Guid tid, Guid cid, Guid petId)
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

    private static Treatment ExistingTreatment(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddDays(-2),
            "Eski",
            "Eski açıklama",
            null,
            null);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TreatmentNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment?)null);

        var result = await CreateHandler().Handle(
            Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantInactive()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("A");
        tenant.Deactivate();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await CreateHandler().Handle(
            Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _treatmentsRead.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingTreatment(tid, cid, petId));

        var result = await CreateHandler().Handle(Cmd(txId, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicIdMissing_And_NoContextClinic()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingTreatment(tid, cid, petId));

        var cmd = new UpdateTreatmentCommand(
            txId,
            Guid.Empty,
            petId,
            null,
            DateTime.UtcNow,
            "T",
            "D",
            null,
            null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.Validation");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DateTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingTreatment(tid, cid, petId));
        SetupTenantClinicPet(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(txId, cid, petId, treatmentDate: DateTime.UtcNow.AddDays(-10)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.DateTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_FollowUpBeforeTreatment()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var at = DateTime.UtcNow.AddHours(-1);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingTreatment(tid, cid, petId));
        SetupTenantClinicPet(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(txId, cid, petId, treatmentDate: at, followUp: at.AddHours(-2)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.FollowUpBeforeTreatment");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationPetMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingTreatment(tid, cid, petId));
        SetupTenantClinicPet(tid, cid, petId);

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateHandler().Handle(
            Cmd(txId, cid, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_Update_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var tx = ExistingTreatment(tid, cid, petId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        SetupTenantClinicPet(tid, cid, petId);

        var exam = new Examination(tid, cid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        _treatmentsWrite.Setup(r => r.UpdateAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _treatmentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            Cmd(tx.Id, cid, petId, examinationId: examId, followUp: DateTime.UtcNow.AddDays(2)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tx.ExaminationId.Should().Be(examId);
        _treatmentsWrite.Verify(r => r.UpdateAsync(tx, It.IsAny<CancellationToken>()), Times.Once);
        _treatmentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
