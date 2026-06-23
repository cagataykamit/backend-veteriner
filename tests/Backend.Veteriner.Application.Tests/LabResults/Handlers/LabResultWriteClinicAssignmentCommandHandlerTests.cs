using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.LabResults.Commands.Create;
using Backend.Veteriner.Application.LabResults.Commands.Update;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.LabResults.Handlers;

/// <summary>IDOR-7G: lab result write clinic assignment enforcement unit tests.</summary>
public sealed class LabResultWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<LabResult>> _labResultsRead = new();
    private readonly Mock<IRepository<LabResult>> _labResultsWrite = new();

    private CreateLabResultCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _labResultsWrite.Object);

    private UpdateLabResultCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _labResultsRead.Object,
            _labResultsWrite.Object);

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

    private static CreateLabResultCommand CreateCmd(
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

    private static UpdateLabResultCommand UpdateCmd(Guid id, Guid clinicId, Guid petId, Guid? examinationId = null)
        => new(
            id,
            clinicId,
            petId,
            examinationId,
            DateTime.UtcNow.AddHours(-1),
            "Güncel test",
            "Güncel sonuç",
            null,
            null);

    private static LabResult ExistingRow(Guid tid, Guid cid, Guid petId, string testName = "Eski test")
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddDays(-2),
            testName,
            "Eski sonuç",
            null,
            null);

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenant(tid, unassignedCid, petId);
        _labResultsWrite.Setup(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult lr, CancellationToken _) => lr);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler().Handle(CreateCmd(unassignedCid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _labResultsWrite.Verify(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);
        _labResultsWrite.Setup(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult lr, CancellationToken _) => lr);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _labResultsWrite.Verify(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _labResultsWrite.Setup(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult lr, CancellationToken _) => lr);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        _labResultsWrite.Verify(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_RelatedExamination_InAssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid, petId);

        var exam = new Examination(tid, assignedCid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);
        _labResultsWrite.Setup(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult lr, CancellationToken _) => lr);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(assignedCid, petId, examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_RelatedExamination_InUnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCid, petId);

        var exam = new Examination(tid, unassignedCid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateCreateHandler(scope.Object).Handle(
            CreateCmd(unassignedCid, petId, examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _labResultsWrite.Verify(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _labResultsWrite.Verify(r => r.AddAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>()), Times.Never);
        _labResultsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        SetupTenant(tid, cid, petId);

        var existing = ExistingRow(tid, cid, petId);
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _labResultsWrite.Setup(r => r.UpdateAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, cid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _labResultsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        var existing = ExistingRow(tid, entityCid, petId);
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            UpdateCmd(existing.Id, entityCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        existing.TestName.Should().Be("Eski test");
        _labResultsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        var existing = ExistingRow(tid, assignedCid, petId);
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
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

        var existing = ExistingRow(tid, entityCid, petId);
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
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

        var existing = ExistingRow(tid, entityCid, petId);
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _labResultsWrite.Setup(r => r.UpdateAsync(It.IsAny<LabResult>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _labResultsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(existing.Id, targetCid, petId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_LabResultMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult?)null);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
    }

    [Fact]
    public async Task Update_Should_ReturnExaminationPetMismatch_When_PetLookupFails_AfterScopeCheck()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tid, cid, petId);

        var existing = ExistingRow(tid, cid, petId);
        _labResultsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var exam = new Examination(tid, cid, Guid.NewGuid(), null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        var result = await CreateUpdateHandler().Handle(
            UpdateCmd(existing.Id, cid, petId, examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.ExaminationPetMismatch");
        _labResultsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
