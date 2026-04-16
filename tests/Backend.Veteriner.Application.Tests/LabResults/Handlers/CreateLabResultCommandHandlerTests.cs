using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.LabResults.Commands.Create;
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

public sealed class CreateLabResultCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IRepository<LabResult>> _labResultsWrite = new();

    private CreateLabResultCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _labResultsWrite.Object);

    private static CreateLabResultCommand Cmd(
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null,
        DateTime? resultDate = null)
        => new(
            clinicId,
            petId,
            examinationId,
            resultDate ?? DateTime.UtcNow.AddHours(-1),
            "Test adı",
            "Sonuç metni",
            null,
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
    public async Task Handle_Should_ReturnFailure_When_TenantNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
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

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _clinics.Verify(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
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
        result.Error.Code.Should().Be("LabResults.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicIdMissing_And_NoContextClinic()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var cmd = new CreateLabResultCommand(
            Guid.Empty,
            Guid.NewGuid(),
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
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);

        var cmd = Cmd(cid, petId, resultDate: DateTime.UtcNow.AddDays(-10));

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.DateTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DateTooFarInFuture()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);

        var cmd = Cmd(cid, petId, resultDate: DateTime.UtcNow.AddYears(3));

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.DateTooFarInFuture");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(Cmd(cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PetNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await CreateHandler().Handle(Cmd(cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(Cmd(cid, petId, examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationClinicMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);

        var exam = new Examination(tid, Guid.NewGuid(), petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateHandler().Handle(Cmd(cid, petId, examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ExaminationClinicMismatch");
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

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateHandler().Handle(Cmd(cid, petId, examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_Persist_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        SetupTenantClinicPetSafe(tid, cid, petId);

        var exam = new Examination(tid, cid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        LabResult? added = null;
        _labResultsWrite.Setup(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()))
            .Callback<LabResult, CancellationToken>((p, _) => added = p)
            .ReturnsAsync((LabResult p, CancellationToken _) => p);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            Cmd(cid, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.ExaminationId.Should().Be(examId);
        _labResultsWrite.Verify(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Once);
        _labResultsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
