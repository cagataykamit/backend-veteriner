using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Handlers;

public sealed class DischargeHospitalizationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IRepository<Hospitalization>> _hospitalizationsWrite = new();

    private DischargeHospitalizationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _hospitalizationsWrite.Object);

    private static Hospitalization ActiveStay(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddHours(-5),
            null,
            "Neden",
            "Eski not");

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(Guid.NewGuid(), DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
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

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(Guid.NewGuid(), DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _hospitalizationsWrite.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(Guid.NewGuid(), DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_MaskAsNotFound_When_ActiveClinicContext_DoesNotMatch_RowClinic()
    {
        var tid = Guid.NewGuid();
        var ctxCid = Guid.NewGuid();
        var rowCid = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(ctxCid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = ActiveStay(tid, rowCid, petId);
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DischargedBeforeAdmission()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var admitted = DateTime.UtcNow.AddHours(-2);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = new Hospitalization(tid, cid, petId, null, admitted, null, "R", null);
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, admitted.AddHours(-1), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.DischargedBeforeAdmission");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AlreadyDischarged()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = ActiveStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!
            .SetValue(row, DateTime.UtcNow.AddHours(-1));

        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AlreadyDischarged");
    }

    [Fact]
    public async Task Handle_Should_Discharge_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = ActiveStay(tid, cid, petId);
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var dischargedAt = DateTime.UtcNow;
        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, dischargedAt, "Taburcu notu"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.DischargedAtUtc.Should().Be(dischargedAt);
        row.DischargeNote.Should().Be("Taburcu notu");
        row.Notes.Should().Be("Eski not");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_LeaveDischargeNoteUnchanged_When_NoteNull()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var row = ActiveStay(tid, cid, petId);
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, DischargeNote: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.Notes.Should().Be("Eski not");
        row.DischargeNote.Should().BeNull();
    }

    private Hospitalization ArrangeActive()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var row = ActiveStay(tid, cid, Guid.NewGuid());
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return row;
    }

    [Fact]
    public async Task Handle_Should_Return_RowVersion_And_Apply_Client_Version()
    {
        var row = ArrangeActive();
        var sent = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 };

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null, Convert.ToBase64String(sent)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        row.RowVersion.Should().Equal(sent);
        result.Value!.RowVersion.Should().Be(Convert.ToBase64String(sent));
    }

    [Fact]
    public async Task Handle_Should_Reject_Malformed_RowVersion()
    {
        var row = ArrangeActive();

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, null, "xx"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.Validation");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_ConcurrencyConflict_When_Save_Detects_Newer_Version()
    {
        var row = ArrangeActive();
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(
            new DischargeHospitalizationCommand(row.Id, DateTime.UtcNow, "Not"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ConcurrencyConflict");
    }
}
