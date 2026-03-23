using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Commands.Create;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Handlers;

public sealed class CreateVaccinationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IRepository<Vaccination>> _vaccinationsWrite = new();

    private CreateVaccinationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _vaccinationsWrite.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var cmd = new CreateVaccinationCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(7),
            null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClinicNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(7),
            null);

        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PetNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            Guid.NewGuid(),
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(7),
            null);

        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationNotInTenant()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            eid,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(7),
            null);

        SetupTenantClinicPet(tid, cid, pid);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
        _vaccinationsWrite.Verify(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ExaminationPetClinicMismatch()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            eid,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(7),
            null);

        SetupTenantClinicPet(tid, cid, pid);
        var wrongExam = new Examination(
            tid,
            Guid.NewGuid(),
            pid,
            null,
            DateTime.UtcNow.AddHours(-1),
            "Şikayet",
            "Bulgu",
            null,
            null);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrongExam);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.ExaminationPetClinicMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ScheduledWithoutDue()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            null,
            null);

        SetupTenantClinicPet(tid, cid, pid);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.ScheduledRequiresDueAt");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ScheduledWithAppliedAt()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddDays(7),
            null);

        SetupTenantClinicPet(tid, cid, pid);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.ScheduledMustNotHaveAppliedAt");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppliedWithoutAppliedAt()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            "Kuduz",
            VaccinationStatus.Applied,
            null,
            null,
            null);

        SetupTenantClinicPet(tid, cid, pid);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.AppliedRequiresAppliedAt");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_CancelledWithAppliedAt()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            "Kuduz",
            VaccinationStatus.Cancelled,
            DateTime.UtcNow.AddHours(-1),
            null,
            null);

        SetupTenantClinicPet(tid, cid, pid);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.CancelledMustNotHaveAppliedAt");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AppliedTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            "Kuduz",
            VaccinationStatus.Applied,
            DateTime.UtcNow.AddDays(-30),
            null,
            null);

        SetupTenantClinicPet(tid, cid, pid);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.AppliedTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DueTooFarInFuture()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddYears(6),
            null);

        SetupTenantClinicPet(tid, cid, pid);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.DueTooFarInFuture");
    }

    [Fact]
    public async Task Handle_Should_CreateScheduled_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var due = DateTime.UtcNow.AddDays(14);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            null,
            " Karma aşı ",
            VaccinationStatus.Scheduled,
            null,
            due,
            "  not  ");

        SetupTenantClinicPet(tid, cid, pid);

        Vaccination? captured = null;
        _vaccinationsWrite.Setup(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()))
            .Callback<Vaccination, CancellationToken>((v, _) => captured = v);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(VaccinationStatus.Scheduled);
        captured.VaccineName.Should().Be("Karma aşı");
        captured.Notes.Should().Be("not");
        captured.AppliedAtUtc.Should().BeNull();
        captured.DueAtUtc.Should().NotBeNull();
        _vaccinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_CreateApplied_WithExamination_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var applied = DateTime.UtcNow.AddHours(-3);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var cmd = new CreateVaccinationCommand(
            cid,
            pid,
            eid,
            "Kuduz",
            VaccinationStatus.Applied,
            applied,
            null,
            null);

        SetupTenantClinicPet(tid, cid, pid);
        var exam = new Examination(
            tid,
            cid,
            pid,
            null,
            DateTime.UtcNow.AddHours(-4),
            "Şikayet",
            "Bulgu",
            null,
            null);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        Vaccination? captured = null;
        _vaccinationsWrite.Setup(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()))
            .Callback<Vaccination, CancellationToken>((v, _) => captured = v);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.ExaminationId.Should().Be(eid, "komutta verilen muayene kimliği saklanır");
        captured.Status.Should().Be(VaccinationStatus.Applied);
    }

    private void SetupTenantClinicPet(Guid tid, Guid cid, Guid pid)
    {
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
    }
}
