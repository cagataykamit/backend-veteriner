using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

/// <summary>IDOR-7B: examination write clinic assignment enforcement unit tests.</summary>
public sealed class ExaminationWriteClinicAssignmentCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();
    private readonly Mock<IReadRepository<Examination>> _examinationsRead = new();
    private readonly Mock<IRepository<Examination>> _examinationsWrite = new();

    private static readonly DateTime ValidExaminedAt = DateTime.UtcNow.AddHours(-1);

    private CreateExaminationCommandHandler CreateCreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointments.Object,
            _appointmentsWrite.Object,
            _examinationsWrite.Object);

    private UpdateExaminationCommandHandler CreateUpdateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointments.Object,
            _examinationsRead.Object,
            _examinationsWrite.Object);

    private void SetupTenant(Guid tid, Guid cid)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        var clinic = new Clinic(tid, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));
    }

    [Fact]
    public async Task Create_Should_Succeed_When_TenantWide_DefaultResolver()
    {
        var tid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        SetupTenant(tid, unassignedCid);

        var result = await CreateCreateHandler().Handle(
            new CreateExaminationCommand(unassignedCid, pid, null, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_NonTenantWide_AssignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(assignedCid, pid, null, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_NonTenantWide_UnassignedClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(unassignedCid, pid, null, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_AppointmentBased_AssignedAppointmentClinic()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid);

        var appt = new Appointment(tid, assignedCid, pid, DateTime.UtcNow.AddDays(1), 30, AppointmentType.Consultation, AppointmentStatus.Scheduled, null);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(null, null, aid, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Completed);
        _appointmentsWrite.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_AppointmentBased_UnassignedAppointmentClinic_NoSideEffect()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCid);

        var appt = new Appointment(tid, unassignedCid, pid, DateTime.UtcNow.AddDays(1), 30, AppointmentType.Consultation, AppointmentStatus.Scheduled, null);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(null, null, aid, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        appt.Status.Should().Be(AppointmentStatus.Scheduled);
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
        _appointmentsWrite.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Succeed_When_ActiveClinicContext_Assigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, assignedCid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(assignedCid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(null, pid, null, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_ReturnAccessDenied_When_ActiveClinicContext_Unassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var unassignedCtx = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });
        SetupTenant(tid, unassignedCtx);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(unassignedCtx);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(null, pid, null, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_NotMutate_When_ResolverFailure()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.Default();
        scope.SetupAccessDenied();
        SetupTenant(tid, cid);

        var result = await CreateCreateHandler(scope.Object).Handle(
            new CreateExaminationCommand(cid, pid, null, ValidExaminedAt, "Şikayet", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
        _appointmentsWrite.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_NonTenantWide_EntityClinicAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, cid, pid, null, ValidExaminedAt, "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, pid, "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateExaminationCommand(eid, cid, pid, null, ValidExaminedAt, "Yeni", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _examinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_NonTenantWide_EntityClinicUnassigned_NoMutation()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var entityCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, entityCid, pid, null, ValidExaminedAt, "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateExaminationCommand(eid, entityCid, pid, null, ValidExaminedAt, "Yeni", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        existing.VisitReason.Should().Be("Old");
        _examinationsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnAccessDenied_When_TargetClinicUnassigned()
    {
        var tid = Guid.NewGuid();
        var assignedCid = Guid.NewGuid();
        var targetCid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assignedCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, assignedCid, pid, null, ValidExaminedAt, "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateExaminationCommand(eid, targetCid, pid, null, ValidExaminedAt, "Yeni", "Bulgu", null, null),
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
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { activeCid });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(activeCid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, entityCid, pid, null, ValidExaminedAt, "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler(scope.Object).Handle(
            new UpdateExaminationCommand(eid, null, pid, null, ValidExaminedAt, "Yeni", "Bulgu", null, null),
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
        var pid = Guid.NewGuid();
        var eid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, entityCid, pid, null, ValidExaminedAt, "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "X"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, pid, "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateUpdateHandler().Handle(
            new UpdateExaminationCommand(eid, targetCid, pid, null, ValidExaminedAt, "Yeni", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ClinicId.Should().Be(targetCid);
    }

    [Fact]
    public async Task Update_Should_ReturnNotFound_When_ExaminationMissing()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdateExaminationCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ValidExaminedAt, "Yeni", "Bulgu", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }
}
