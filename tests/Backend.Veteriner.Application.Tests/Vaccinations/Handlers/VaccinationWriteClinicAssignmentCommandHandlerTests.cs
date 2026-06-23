using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Vaccinations.Commands.Create;
using Backend.Veteriner.Application.Vaccinations.Commands.Update;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Handlers;

/// <summary>IDOR-7E: vaccination write clinic assignment enforcement unit tests.</summary>
public sealed class VaccinationWriteClinicAssignmentCommandHandlerTests
{
    private static readonly VaccineDefinition TestDefinition = VaccineDefinition.CreateGlobal("TSTX", "Karma aşı");
    private static readonly DateTime ValidDueAt = DateTime.UtcNow.AddDays(7);

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<VaccineDefinition>> _definitions = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinationsRead = new();
    private readonly Mock<IRepository<Vaccination>> _vaccinationsWrite = new();

    public VaccinationWriteClinicAssignmentCommandHandlerTests()
    {
        _definitions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccineDefinitionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestDefinition);
    }

    private CreateVaccinationCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _definitions.Object,
            _vaccinationsWrite.Object);

    private UpdateVaccinationCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _definitions.Object,
            _vaccinationsRead.Object,
            _vaccinationsWrite.Object);

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

    private static CreateVaccinationCommand CreateCmd(Guid clinicId, Guid petId)
        => new(
            clinicId,
            petId,
            null,
            TestDefinition.Id,
            VaccinationStatus.Scheduled,
            null,
            ValidDueAt,
            null);

    private static UpdateVaccinationCommand UpdateCmd(Guid id, Guid clinicId, Guid petId)
        => new(
            id,
            clinicId,
            petId,
            null,
            TestDefinition.Id,
            VaccinationStatus.Scheduled,
            null,
            ValidDueAt,
            null);

    private static Vaccination ExistingVaccination(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            petId,
            cid,
            null,
            TestDefinition.Id,
            TestDefinition.Name,
            VaccinationStatus.Scheduled,
            null,
            ValidDueAt.AddDays(-1),
            "Eski");

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, unassignedCid, petId);
        _vaccinationsWrite.Setup(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vaccination v, CancellationToken _) => v);
        _vaccinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler().Handle(CreateCmd(unassignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _vaccinationsWrite.Verify(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _vaccinationsWrite.Setup(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vaccination v, CancellationToken _) => v);
        _vaccinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _vaccinationsWrite.Verify(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _vaccinationsWrite.Setup(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vaccination v, CancellationToken _) => v);
        _vaccinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _vaccinationsWrite.Verify(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _vaccinationsWrite.Verify(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Never);
        _vaccinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_ReturnVaccineDefinitionNotFound_When_DefinitionMissing_AfterScopeCheck()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, cid, petId);
        _definitions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccineDefinitionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaccineDefinition?)null);

        var result = await CreateCreateHandler().Handle(CreateCmd(cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VaccineDefinitions.NotFound");
        _vaccinationsWrite.Verify(r => r.AddAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        SetupTenant(tid, cid, petId);

        var v = ExistingVaccination(tid, cid, petId);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);
        _vaccinationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _vaccinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(v.Id, cid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _vaccinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        var v = ExistingVaccination(tid, entityCid, petId);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(v.Id, entityCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        v.Notes.Should().Be("Eski");
        _vaccinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        var v = ExistingVaccination(tid, assignedCid, petId);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(v.Id, targetCid, petId),
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

        var v = ExistingVaccination(tid, entityCid, petId);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(v.Id, activeCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        v.ClinicId.Should().Be(entityCid);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, targetCid, petId);

        var v = ExistingVaccination(tid, entityCid, petId);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);
        _vaccinationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _vaccinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(v.Id, targetCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        v.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_VaccinationMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vaccination?)null);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.NotFound");
    }

    [Fact]
    public async Task Update_Should_ReturnVaccineDefinitionNotFound_When_DefinitionMissing_AfterScopeCheck()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, cid, petId);
        _definitions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccineDefinitionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VaccineDefinition?)null);

        var v = ExistingVaccination(tid, cid, petId);
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(v.Id, cid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VaccineDefinitions.NotFound");
        _vaccinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
