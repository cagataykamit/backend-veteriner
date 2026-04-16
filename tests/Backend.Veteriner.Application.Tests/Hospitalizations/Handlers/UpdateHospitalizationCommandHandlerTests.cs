using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Commands.Update;
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

public sealed class UpdateHospitalizationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizationsRead = new();
    private readonly Mock<IRepository<Hospitalization>> _hospitalizationsWrite = new();

    private UpdateHospitalizationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _hospitalizationsRead.Object,
            _hospitalizationsWrite.Object);

    private static UpdateHospitalizationCommand Cmd(
        Guid id,
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null,
        DateTime? admittedAt = null,
        DateTime? planned = null)
        => new(
            id,
            clinicId,
            petId,
            examinationId,
            admittedAt ?? DateTime.UtcNow.AddHours(-1),
            planned,
            "Güncel neden",
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

    private static Hospitalization OpenStay(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            null,
            "Eski",
            null);

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
        _hospitalizationsRead.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AlreadyDischarged()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = OpenStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(existing, hid);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!
            .SetValue(existing, DateTime.UtcNow);

        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(Cmd(hid, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AlreadyDischarged");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AdmittedAtTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = OpenStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(existing, hid);

        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(hid, cid, petId, admittedAt: DateTime.UtcNow.AddDays(-10)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AdmittedAtTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ActiveHospitalizationExists()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = OpenStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(existing, hid);

        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(Cmd(hid, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ActiveHospitalizationExists");
    }

    [Fact]
    public async Task Handle_Should_Update_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exam = new Examination(tid, cid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ExaminationId.Should().Be(examId);
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }
}
