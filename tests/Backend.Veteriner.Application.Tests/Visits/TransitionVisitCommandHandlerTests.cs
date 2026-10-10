using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Commands.Transition;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class TransitionVisitCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Visit>> _visitsRead = new();
    private readonly Mock<IRepository<Visit>> _visitsWrite = new();

    public TransitionVisitCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
    }

    private TransitionVisitCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _visitsRead.Object,
            _visitsWrite.Object,
            VisitHandlerTestSupport.FixedClock,
            VeterinarianReaderMock.Create().Object);

    private Visit WaitingVisit() => VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);

    private void ReturnVisit(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    [Fact]
    public async Task Handle_Start_Should_Move_To_InProgress_And_Enqueue_Event()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.InProgress), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CareStatus.Should().Be(VisitCareStatus.InProgress);
        result.Value.StartedAtUtc.Should().Be(VisitHandlerTestSupport.FixedNowUtc);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Start_When_Already_InProgress_Should_Be_Idempotent_Without_Side_Effects()
    {
        var visit = WaitingVisit();
        visit.Start(VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.InProgress), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CareStatus.Should().Be(VisitCareStatus.InProgress);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Complete_Should_Fail_When_Visit_Is_Waiting()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.Completed), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.InvalidStatusTransition");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_When_Visit_Missing_Or_Voided()
    {
        ReturnVisit(null);
        var missing = await CreateHandler().Handle(
            new TransitionVisitCommand(Guid.NewGuid(), VisitCareStatus.InProgress), CancellationToken.None);
        missing.Error.Code.Should().Be("Visits.NotFound");

        var voided = WaitingVisit();
        voided.MarkAsMistaken("Yanlis gelis kaydi", VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(voided);
        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(voided.Id, VisitCareStatus.InProgress), CancellationToken.None);
        result.Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_When_Visit_Belongs_To_Other_Clinic_Context()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.InProgress), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.InProgress), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Return_Success_When_Concurrent_Request_Already_Reached_Target()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        var persisted = WaitingVisit();
        persisted.Start(VisitHandlerTestSupport.FixedNowUtc);
        _visitsRead.Setup(r => r.FirstOrDefaultAsync(
                It.Is<VisitByIdSpec>(s => s.AsNoTracking), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.InProgress), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CareStatus.Should().Be(VisitCareStatus.InProgress);
    }

    [Fact]
    public async Task Handle_Should_Return_ConcurrencyConflict_When_Target_Not_Reached_After_Conflict()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        _visitsRead.Setup(r => r.FirstOrDefaultAsync(
                It.Is<VisitByIdSpec>(s => s.AsNoTracking), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WaitingVisit());
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(
            new TransitionVisitCommand(visit.Id, VisitCareStatus.InProgress), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.ConcurrencyConflict");
    }
}
