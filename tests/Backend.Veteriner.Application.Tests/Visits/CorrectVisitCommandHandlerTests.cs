using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Commands.Correct;
using Backend.Veteriner.Application.Visits.IntegrationEvents;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class CorrectVisitCommandHandlerTests
{
    private const string Reason = "Yanlis hasta secildi";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Visit>> _visitsRead = new();
    private readonly Mock<IRepository<Visit>> _visitsWrite = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IVisitIntegrationEventOutbox> _outbox = new();

    public CorrectVisitCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _examinations.Setup(r => r.AnyAsync(It.IsAny<ExaminationsByVisitIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private CorrectVisitCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _visitsRead.Object,
            _visitsWrite.Object,
            _examinations.Object,
            _outbox.Object,
            VisitHandlerTestSupport.FixedClock);

    private Visit CompletedVisit()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        visit.Start(VisitHandlerTestSupport.FixedNowUtc);
        visit.Complete(VisitHandlerTestSupport.FixedNowUtc);
        return visit;
    }

    private void ReturnVisit(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    private void ReturnActiveVisitForPet(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ActiveVisitByPetIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    [Fact]
    public async Task Handle_Should_Fail_When_Neither_Target_Nor_MarkAsMistaken_Given()
    {
        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(Guid.NewGuid(), Reason, null, false), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Both_Target_And_MarkAsMistaken_Given()
    {
        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(Guid.NewGuid(), Reason, VisitCareStatus.Waiting, true), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Reason_Missing_Without_Saving()
    {
        var visit = CompletedVisit();
        ReturnVisit(visit);
        ReturnActiveVisitForPet(null);

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, "  ", VisitCareStatus.InProgress, false), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_Reopen_Completed_Visit_And_Enqueue_Event()
    {
        var visit = CompletedVisit();
        ReturnVisit(visit);
        ReturnActiveVisitForPet(null);

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, Reason, VisitCareStatus.InProgress, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CareStatus.Should().Be(VisitCareStatus.InProgress);
        result.Value.CompletedAtUtc.Should().BeNull();
        _outbox.Verify(
            o => o.EnqueueAsync(
                VisitIntegrationEventTypes.Updated,
                It.IsAny<VisitUpdatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Return_DuplicateActiveVisit_When_Pet_Has_Another_Active_Visit()
    {
        var visit = CompletedVisit();
        ReturnVisit(visit);
        ReturnActiveVisitForPet(VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, visit.PetId));

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, Reason, VisitCareStatus.Waiting, false), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.DuplicateActiveVisit");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_DuplicateActiveVisit_When_Unique_Index_Rejects_Reopen()
    {
        var visit = CompletedVisit();
        ReturnVisit(visit);
        ReturnActiveVisitForPet(null);
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("unique"));

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, Reason, VisitCareStatus.Waiting, false), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.DuplicateActiveVisit");
    }

    [Fact]
    public async Task Handle_Should_Mark_Visit_As_Mistaken_With_Reason()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, Reason, null, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsVoided.Should().BeTrue();
        result.Value.VoidReason.Should().Be(Reason);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Reject_MarkAsMistaken_When_Visit_Has_Examinations()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        ReturnVisit(visit);
        _examinations.Setup(r => r.AnyAsync(It.IsAny<ExaminationsByVisitIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, Reason, null, true), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.HasExaminations");
        visit.IsVoided.Should().BeFalse();
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Missing_Or_Already_Voided_Visit()
    {
        ReturnVisit(null);
        var missing = await CreateHandler().Handle(
            new CorrectVisitCommand(Guid.NewGuid(), Reason, null, true), CancellationToken.None);
        missing.Error.Code.Should().Be("Visits.NotFound");

        var voided = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        voided.MarkAsMistaken(Reason, VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(voided);
        var again = await CreateHandler().Handle(
            new CorrectVisitCommand(voided.Id, Reason, null, true), CancellationToken.None);
        again.Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        var visit = CompletedVisit();
        ReturnVisit(visit);
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(
            new CorrectVisitCommand(visit.Id, Reason, VisitCareStatus.Waiting, false), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public void Command_Should_Expose_Audit_Action_And_Target()
    {
        var id = Guid.NewGuid();
        var command = new CorrectVisitCommand(id, Reason, null, true);

        command.AuditAction.Should().Be("Visit.Correct");
        command.AuditTarget.Should().Be($"VisitId={id}");
        command.Should().BeAssignableTo<IAuditableRequest>();
    }
}
