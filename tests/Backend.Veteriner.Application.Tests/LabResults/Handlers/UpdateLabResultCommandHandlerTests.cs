using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.LabResults.Commands.Update;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.LabResults.Handlers;

public sealed class UpdateLabResultCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<LabResult>> _labResultsRead = new();
    private readonly Mock<IRepository<LabResult>> _labResultsWrite = new();

    private UpdateLabResultCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _labResultsRead.Object,
            _labResultsWrite.Object);

    private static UpdateLabResultCommand Cmd(
        Guid id,
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null,
        DateTime? resultDate = null)
        => new(
            id,
            clinicId,
            petId,
            examinationId,
            resultDate ?? DateTime.UtcNow.AddHours(-1),
            "Test adı",
            "Sonuç metni",
            null,
            null);

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

    private static LabResult ExistingLabResult(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddDays(-2),
            "Eski test",
            "Eski sonuç",
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
    public async Task Handle_Should_ReturnFailure_When_LabResultNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult?)null);

        var result = await CreateHandler().Handle(
            Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
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
        _labResultsRead.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var lrId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingLabResult(tid, cid, petId));

        var result = await CreateHandler().Handle(Cmd(lrId, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicIdMissing_And_NoContextClinic()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var lrId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingLabResult(tid, cid, petId));

        var cmd = new UpdateLabResultCommand(
            lrId,
            Guid.Empty,
            petId,
            null,
            DateTime.UtcNow,
            "T",
            "R",
            null,
            null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.Validation");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DateTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var lrId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingLabResult(tid, cid, petId));
        SetupTenantClinicPet(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(lrId, cid, petId, resultDate: DateTime.UtcNow.AddDays(-10)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.DateTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationPetMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var lrId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingLabResult(tid, cid, petId));
        SetupTenantClinicPet(tid, cid, petId);

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateHandler().Handle(
            Cmd(lrId, cid, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_Update_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var row = ExistingLabResult(tid, cid, petId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        SetupTenantClinicPet(tid, cid, petId);

        var exam = new Examination(tid, cid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        _labResultsWrite.Setup(r => r.UpdateAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            Cmd(row.Id, cid, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.ExaminationId.Should().Be(examId);
        _labResultsWrite.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        _labResultsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
