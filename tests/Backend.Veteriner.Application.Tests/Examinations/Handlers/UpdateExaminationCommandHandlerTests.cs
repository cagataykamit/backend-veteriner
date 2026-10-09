using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class UpdateExaminationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IRepository<Examination>> _examinationsWrite = new();

    private UpdateExaminationCommandHandler CreateHandler(IClinicReadScopeResolver? resolver = null)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver ?? _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinationsWrite.Object);

    private static UpdateExaminationCommand BuildCommand(
        Guid id,
        Guid? clinicId = null,
        Guid? petId = null,
        Guid? appointmentId = null,
        string? rowVersion = null)
    {
        rowVersion ??= ExaminationTestSupport.SampleRowVersionBase64;
        return new UpdateExaminationCommand(
            id,
            clinicId,
            petId,
            appointmentId,
            DateTime.UtcNow,
            "Sikayet",
            string.Empty,
            null,
            null,
            rowVersion);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RouteEntityNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        _examinationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var result = await CreateHandler().Handle(BuildCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RowVersionMissingOrInvalid()
    {
        var tid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var cmd = new UpdateExaminationCommand(
            eid, null, null, null, DateTime.UtcNow, "Sikayet", string.Empty, null, null, null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Validation");
    }

    [Fact]
    public async Task Handle_Should_PreserveAppointment_When_AppointmentIdOmitted()
    {
        var tid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, cid, pid, aid, DateTime.UtcNow.AddHours(-1), "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        ExaminationTestSupport.SetRowVersion(existing);

        _examinationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "Istanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var result = await CreateHandler().Handle(
            BuildCommand(eid, appointmentId: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.AppointmentId.Should().Be(aid);
        existing.ClinicId.Should().Be(cid);
        existing.PetId.Should().Be(pid);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_AppointmentChangeRequested()
    {
        var tid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, cid, pid, null, DateTime.UtcNow.AddHours(-1), "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        ExaminationTestSupport.SetRowVersion(existing);

        _examinationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(
            BuildCommand(eid, appointmentId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.AppointmentChangeNotAllowed");
    }

    [Fact]
    public async Task Handle_Should_Map_DbUpdateConcurrencyException_To_ConcurrencyConflict()
    {
        var tid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, cid, pid, null, DateTime.UtcNow.AddHours(-1), "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        ExaminationTestSupport.SetRowVersion(existing);

        _examinationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "Istanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        _examinationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", innerException: null));

        var result = await CreateHandler().Handle(BuildCommand(eid, cid, pid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ExaminedTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, cid, pid, null, DateTime.UtcNow.AddHours(-1), "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);
        ExaminationTestSupport.SetRowVersion(existing);

        _examinationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var cmd = new UpdateExaminationCommand(
            eid,
            cid,
            pid,
            null,
            DateTime.UtcNow.AddDays(-30),
            "Sikayet",
            "Bulgu",
            null,
            null,
            ExaminationTestSupport.SampleRowVersionBase64);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.ExaminedTooFarInPast");
    }
}
