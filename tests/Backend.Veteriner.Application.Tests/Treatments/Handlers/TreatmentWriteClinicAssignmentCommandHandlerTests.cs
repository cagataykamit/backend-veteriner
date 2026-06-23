using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Treatments.Commands.Create;
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

/// <summary>IDOR-7C: treatment write clinic assignment enforcement unit tests.</summary>
public sealed class TreatmentWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Treatment>> _treatmentsRead = new();
    private readonly Mock<IRepository<Treatment>> _treatmentsWrite = new();

    private static readonly DateTime ValidTreatmentAt = DateTime.UtcNow.AddHours(-1);

    private CreateTreatmentCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _treatmentsWrite.Object);

    private UpdateTreatmentCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _treatmentsRead.Object,
            _treatmentsWrite.Object);

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

    private static CreateTreatmentCommand CreateCmd(
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null)
        => new(clinicId, petId, examinationId, ValidTreatmentAt, "Başlık", "Açıklama", null, null);

    private static UpdateTreatmentCommand UpdateCmd(
        Guid id,
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null)
        => new(id, clinicId, petId, examinationId, ValidTreatmentAt, "Başlık", "Açıklama", null, null);

    private static Treatment ExistingTreatment(Guid tid, Guid cid, Guid petId)
        => new(tid, cid, petId, null, ValidTreatmentAt.AddDays(-1), "Eski", "Eski açıklama", null, null);

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, unassignedCid, petId);

        var result = await CreateCreateHandler().Handle(CreateCmd(unassignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _treatmentsWrite.Verify(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _treatmentsWrite.Setup(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment p, CancellationToken _) => p);
        _treatmentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _treatmentsWrite.Verify(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_ExaminationLinked_AssignedExaminationClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(assignedCid);

        var exam = new Examination(tid, assignedCid, petId, null, ValidTreatmentAt, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);
        _treatmentsWrite.Setup(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment p, CancellationToken _) => p);
        _treatmentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(assignedCid, petId, examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_ExaminationLinked_UnassignedExaminationClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCid, petId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(unassignedCid);

        var exam = new Examination(tid, unassignedCid, petId, null, ValidTreatmentAt, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(unassignedCid, petId, examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _treatmentsWrite.Verify(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _treatmentsWrite.Setup(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment p, CancellationToken _) => p);
        _treatmentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _treatmentsWrite.Verify(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _treatmentsWrite.Verify(r => r.AddAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>()), Times.Never);
        _treatmentsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        SetupTenant(tid, cid, petId);

        var tx = ExistingTreatment(tid, cid, petId);
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        _treatmentsWrite.Setup(r => r.UpdateAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _treatmentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(tx.Id, cid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _treatmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        var tx = ExistingTreatment(tid, entityCid, petId);
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(tx.Id, entityCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        tx.Title.Should().Be("Eski");
        _treatmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        var tx = ExistingTreatment(tid, assignedCid, petId);
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(tx.Id, targetCid, petId),
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

        var tx = ExistingTreatment(tid, entityCid, petId);
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(tx.Id, activeCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        tx.ClinicId.Should().Be(entityCid);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, targetCid, petId);

        var tx = ExistingTreatment(tid, entityCid, petId);
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        _treatmentsWrite.Setup(r => r.UpdateAsync(It.IsAny<Treatment>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _treatmentsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(tx.Id, targetCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tx.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_TreatmentMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _treatmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment?)null);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
    }
}
