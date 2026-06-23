using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Commands.Create;
using Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;
using Backend.Veteriner.Application.Hospitalizations.Commands.Update;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Handlers;

/// <summary>IDOR-7F: hospitalization write clinic assignment enforcement unit tests.</summary>
public sealed class HospitalizationWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizationsRead = new();
    private readonly Mock<IRepository<Hospitalization>> _hospitalizationsWrite = new();

    private CreateHospitalizationCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _hospitalizationsRead.Object,
            _hospitalizationsWrite.Object);

    private UpdateHospitalizationCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _hospitalizationsRead.Object,
            _hospitalizationsWrite.Object);

    private DischargeHospitalizationCommandHandler CreateDischargeHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _hospitalizationsRead.Object,
            _hospitalizationsWrite.Object);

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
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private static CreateHospitalizationCommand CreateCmd(Guid clinicId, Guid petId, Guid? examinationId = null)
        => new(
            clinicId,
            petId,
            examinationId,
            DateTime.UtcNow.AddHours(-1),
            null,
            "Yatış nedeni",
            null);

    private static UpdateHospitalizationCommand UpdateCmd(Guid id, Guid clinicId, Guid petId)
        => new(
            id,
            clinicId,
            petId,
            null,
            DateTime.UtcNow.AddHours(-1),
            null,
            "Güncel neden",
            null);

    private static Hospitalization OpenStay(Guid tid, Guid cid, Guid petId, string reason = "Eski")
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            null,
            reason,
            null);

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, unassignedCid, petId);
        _hospitalizationsWrite.Setup(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization h, CancellationToken _) => h);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler().Handle(CreateCmd(unassignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _hospitalizationsWrite.Verify(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _hospitalizationsWrite.Setup(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization h, CancellationToken _) => h);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _hospitalizationsWrite.Verify(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _hospitalizationsWrite.Setup(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization h, CancellationToken _) => h);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(assignedCid, petId), CancellationToken.None);

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

        var result = await CreateCreateHandler(scope.Object).Handle(CreateCmd(unassignedCtx, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _hospitalizationsWrite.Verify(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _hospitalizationsWrite.Verify(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
        _hospitalizationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_ReturnExaminationPetMismatch_When_ExaminationPetMismatch_AfterScopeCheck()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid, cid, petId);

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateCreateHandler().Handle(CreateCmd(cid, petId, examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ExaminationPetMismatch");
        _hospitalizationsWrite.Verify(r => r.AddAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        SetupTenant(tid, cid, petId);

        var existing = OpenStay(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, cid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _hospitalizationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        var existing = OpenStay(tid, entityCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, entityCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        existing.Reason.Should().Be("Eski");
        _hospitalizationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        var existing = OpenStay(tid, assignedCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, targetCid, petId),
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

        var existing = OpenStay(tid, entityCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, activeCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        existing.ClinicId.Should().Be(entityCid);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, targetCid, petId);

        var existing = OpenStay(tid, entityCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(existing.Id, targetCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_HospitalizationMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Discharge_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = OpenStay(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateDischargeHandler(scope.Object).Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.DischargedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Discharge_Should_ReturnAccessDenied_When_NonTenantWide_EntityClinicUnassigned_NoMutation()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = OpenStay(tid, entityCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await CreateDischargeHandler(scope.Object).Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        row.DischargedAtUtc.Should().BeNull();
        _hospitalizationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Discharge_Should_ReturnAccessDenied_When_ContextNull_And_UnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = OpenStay(tid, entityCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await CreateDischargeHandler(scope.Object).Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        row.DischargedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Discharge_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = OpenStay(tid, entityCid, petId);
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateDischargeHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.DischargedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Discharge_Should_ReturnNotFound_When_HospitalizationMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _hospitalizationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateDischargeHandler().Handle(
            new DischargeHospitalizationCommand(Guid.NewGuid(), DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }
}
