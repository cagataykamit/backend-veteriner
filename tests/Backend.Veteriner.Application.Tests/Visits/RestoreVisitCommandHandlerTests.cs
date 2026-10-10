using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Commands.Restore;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class RestoreVisitCommandHandlerTests
{
    private const string Reason = "Yanlis isaretlendi, geri aliniyor";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Visit>> _visitsRead = new();
    private readonly Mock<IRepository<Visit>> _visitsWrite = new();

    public RestoreVisitCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        ReturnActiveVisitForPet(null);
        ReturnVisitForAppointment(null);
    }

    private RestoreVisitCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _visitsRead.Object,
            _visitsWrite.Object,
            VeterinarianReaderMock.Create().Object);

    private Visit VoidedVisit(Guid? appointmentId = null)
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, appointmentId: appointmentId);
        visit.MarkAsMistaken("Yanlis hasta secildi", VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(visit);
        return visit;
    }

    private void ReturnVisit(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    private void ReturnActiveVisitForPet(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ActiveVisitByPetIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    private void ReturnVisitForAppointment(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByAppointmentIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    [Fact]
    public async Task Handle_Should_Restore_Voided_Visit_And_Save()
    {
        var visit = VoidedVisit();

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsVoided.Should().BeFalse();
        result.Value.VoidReason.Should().BeNull();
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Return_DuplicateActiveVisit_When_Pet_Has_Another_Active_Visit()
    {
        var visit = VoidedVisit();
        ReturnActiveVisitForPet(VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, visit.PetId));

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.DuplicateActiveVisit");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Restore_Completed_Visit_Even_When_Pet_Has_Another_Active_Visit()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        visit.Start(VisitHandlerTestSupport.FixedNowUtc);
        visit.Complete(VisitHandlerTestSupport.FixedNowUtc);
        visit.MarkAsMistaken("Yanlis hasta secildi", VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(visit);
        ReturnActiveVisitForPet(VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, visit.PetId));

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Return_DuplicateAppointmentVisit_When_Appointment_Has_Another_Visit()
    {
        var appointmentId = Guid.NewGuid();
        var visit = VoidedVisit(appointmentId);
        ReturnVisitForAppointment(VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, appointmentId: appointmentId));

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.DuplicateAppointmentVisit");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_Conflict_When_Unique_Index_Rejects_Restore()
    {
        var visit = VoidedVisit();
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ReturnActiveVisitForPet(VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, visit.PetId)))
            .ThrowsAsync(new DbUpdateException("unique"));

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.DuplicateActiveVisit");
    }

    [Fact]
    public async Task Handle_Should_Rethrow_When_Save_Fails_Without_Known_Conflict()
    {
        var visit = VoidedVisit();
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("other"));

        var act = () => CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Handle_Should_Fail_Without_Reason_And_Not_Save()
    {
        var visit = VoidedVisit();

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, " "), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Visit_Is_Not_Voided()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Missing_Or_Other_Clinic_Visit()
    {
        ReturnVisit(null);
        var missing = await CreateHandler().Handle(new RestoreVisitCommand(Guid.NewGuid(), Reason), CancellationToken.None);
        missing.Error.Code.Should().Be("Visits.NotFound");

        var visit = VoidedVisit();
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var otherClinic = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);
        otherClinic.Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        var visit = VoidedVisit();
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(new RestoreVisitCommand(visit.Id, Reason), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Tenant_Missing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new RestoreVisitCommand(Guid.NewGuid(), Reason), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public void Command_Should_Expose_Audit_Action_And_Target()
    {
        var id = Guid.NewGuid();
        var command = new RestoreVisitCommand(id, Reason);

        command.AuditAction.Should().Be("Visit.Restore");
        command.AuditTarget.Should().Be($"VisitId={id}");
        command.Should().BeAssignableTo<IAuditableRequest>();
    }
}
