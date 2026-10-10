using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Commands.SetUrgency;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class SetVisitUrgencyCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Visit>> _visitsRead = new();
    private readonly Mock<IRepository<Visit>> _visitsWrite = new();

    public SetVisitUrgencyCommandHandlerTests()
        => _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);

    private SetVisitUrgencyCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _visitsRead.Object,
            _visitsWrite.Object,
            VeterinarianReaderMock.Create().Object);

    private Visit ReturnVisit(Visit? visit = null)
    {
        visit ??= VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);
        return visit;
    }

    [Fact]
    public async Task Handle_Should_Mark_Urgent_And_Save()
    {
        var visit = ReturnVisit();

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsUrgent.Should().BeTrue();
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Be_Idempotent_Without_Saving_When_Value_Unchanged()
    {
        var visit = ReturnVisit();

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsUrgent.Should().BeFalse();
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_NotOpen_When_Visit_Completed_And_Value_Changes()
    {
        var visit = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        visit.Start(VisitHandlerTestSupport.FixedNowUtc);
        visit.Complete(VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, true), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotOpen");
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_Concurrent_Request_Already_Applied_Same_Value()
    {
        var visit = ReturnVisit();
        var winner = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        winner.SetUrgent(true);
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(winner))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsUrgent.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Return_ConcurrencyConflict_When_Value_Differs_After_Conflict()
    {
        var visit = ReturnVisit();
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId)))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, true), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Missing_Voided_Or_Other_Clinic_Visit()
    {
        _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Visit?)null);
        (await CreateHandler().Handle(new SetVisitUrgencyCommand(Guid.NewGuid(), true), CancellationToken.None))
            .Error.Code.Should().Be("Visits.NotFound");

        var voided = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId);
        voided.MarkAsMistaken("Yanlis hasta secildi", VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(voided);
        (await CreateHandler().Handle(new SetVisitUrgencyCommand(voided.Id, true), CancellationToken.None))
            .Error.Code.Should().Be("Visits.NotFound");

        var visit = ReturnVisit();
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        (await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, true), CancellationToken.None))
            .Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        var visit = ReturnVisit();
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(visit.Id, true), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Tenant_Missing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new SetVisitUrgencyCommand(Guid.NewGuid(), true), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }
}
