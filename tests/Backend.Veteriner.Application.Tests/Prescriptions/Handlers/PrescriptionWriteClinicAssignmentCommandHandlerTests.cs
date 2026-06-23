using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Commands.Create;
using Backend.Veteriner.Application.Prescriptions.Commands.Update;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Prescriptions.Handlers;

/// <summary>IDOR-7D: prescription write clinic assignment enforcement unit tests.</summary>
public sealed class PrescriptionWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptionsRead = new();
    private readonly Mock<IRepository<Prescription>> _prescriptionsWrite = new();

    private static readonly DateTime ValidPrescribedAt = DateTime.UtcNow.AddHours(-1);

    private CreatePrescriptionCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _treatments.Object,
            _prescriptionsWrite.Object);

    private UpdatePrescriptionCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _treatments.Object,
            _prescriptionsRead.Object,
            _prescriptionsWrite.Object);

    private void SetupTenant(Guid tid, Guid cid, Guid petId)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        var clinic = new Clinic(tid, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        var pet = new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
    }

    private static CreatePrescriptionCommand CreateCmd(
        Guid clinicId,
        Guid petId,
        Guid? treatmentId = null,
        Guid? examinationId = null)
        => new(
            clinicId,
            petId,
            examinationId,
            treatmentId,
            ValidPrescribedAt,
            "Başlık",
            "İçerik",
            null,
            null);

    private static UpdatePrescriptionCommand UpdateCmd(
        Guid id,
        Guid clinicId,
        Guid petId,
        Guid? treatmentId = null,
        Guid? examinationId = null)
        => new(
            id,
            clinicId,
            petId,
            examinationId,
            treatmentId,
            ValidPrescribedAt,
            "Başlık",
            "İçerik",
            null,
            null);

    private static Prescription ExistingPrescription(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            cid,
            petId,
            null,
            null,
            ValidPrescribedAt.AddDays(-1),
            "Eski",
            "Eski içerik",
            null,
            null);

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, unassignedCid, petId);
        _prescriptionsWrite.Setup(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription p, CancellationToken _) => p);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler().Handle(CreateCmd(unassignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _prescriptionsWrite.Verify(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _prescriptionsWrite.Setup(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription p, CancellationToken _) => p);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(assignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_NonTenantWide_UnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCid, petId);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(unassignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _prescriptionsWrite.Verify(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_TreatmentLinked_AssignedTreatmentClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(assignedCid);

        var treatment = new Treatment(tid, assignedCid, petId, null, ValidPrescribedAt, "T", "D", null, null);
        typeof(Treatment).GetProperty(nameof(Treatment.Id))!.SetValue(treatment, treatmentId);
        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(treatment);
        _prescriptionsWrite.Setup(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription p, CancellationToken _) => p);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(assignedCid, petId, treatmentId: treatmentId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_TreatmentLinked_UnassignedTreatmentClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCid, petId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(unassignedCid);

        var treatment = new Treatment(tid, unassignedCid, petId, null, ValidPrescribedAt, "T", "D", null, null);
        typeof(Treatment).GetProperty(nameof(Treatment.Id))!.SetValue(treatment, treatmentId);
        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(treatment);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(unassignedCid, petId, treatmentId: treatmentId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _prescriptionsWrite.Verify(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_ActiveClinicContext_Assigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(assignedCid);
        _prescriptionsWrite.Setup(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription p, CancellationToken _) => p);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(assignedCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_ActiveClinicContext_Unassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCtx = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCtx, petId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(unassignedCtx);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(unassignedCtx, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _prescriptionsWrite.Verify(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_NotMutate_When_ResolverFailure()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.Default();
        scope.SetupAccessDenied();
        SetupTenant(tid, cid, petId);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _prescriptionsWrite.Verify(r => r.AddAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
        _prescriptionsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        SetupTenant(tid, cid, petId);

        var rx = ExistingPrescription(tid, cid, petId);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rx);
        _prescriptionsWrite.Setup(r => r.UpdateAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(rx.Id, cid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _prescriptionsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_NonTenantWide_EntityClinicUnassigned_NoMutation()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var rx = ExistingPrescription(tid, entityCid, petId);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rx);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(rx.Id, entityCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        rx.Title.Should().Be("Eski");
        _prescriptionsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_TargetClinicUnassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var rx = ExistingPrescription(tid, assignedCid, petId);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rx);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(rx.Id, targetCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Update_Should_NotPull_Entity_To_ActiveClinic_When_EntityInOtherClinic()
    {
        var tid = Guid.NewGuid();
        var activeCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { activeCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(activeCid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var rx = ExistingPrescription(tid, entityCid, petId);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rx);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(rx.Id, activeCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        rx.ClinicId.Should().Be(entityCid);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, targetCid, petId);

        var rx = ExistingPrescription(tid, entityCid, petId);
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rx);
        _prescriptionsWrite.Setup(r => r.UpdateAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _prescriptionsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(rx.Id, targetCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rx.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_PrescriptionMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _prescriptionsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.NotFound");
    }
}
