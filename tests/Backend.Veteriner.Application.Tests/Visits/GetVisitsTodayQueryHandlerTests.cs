using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetToday;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class GetVisitsTodayQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IVisitTodayReader> _reader = new();

    // FixedNowUtc = 2026-10-10 09:00 UTC → İstanbul 12:00, takvim günü 2026-10-10.
    private static readonly DateOnly Today = new(2026, 10, 10);
    private static readonly DateTime T0 = VisitHandlerTestSupport.FixedNowUtc;

    public GetVisitsTodayQueryHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        Returns([]);
    }

    private GetVisitsTodayQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _reader.Object,
            VisitHandlerTestSupport.FixedClock);

    private void Returns(IReadOnlyList<TodayItemDto> items)
        => _reader.Setup(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VisitTodayReadResult(items, []));

    private static TodayItemDto Visit(
        string pet, VisitCareStatus status, DateTime arrivedAtUtc, DateTime? scheduledAtUtc = null)
        => new(Guid.NewGuid(), null, Guid.NewGuid(), pet, null, Guid.NewGuid(), "Sahip", null,
            scheduledAtUtc, arrivedAtUtc, status, null, null, null, false, TodayPaymentIndicator.NoPaymentRecorded, false);

    private static TodayItemDto Planned(string pet, DateTime scheduledAtUtc)
        => new(null, Guid.NewGuid(), Guid.NewGuid(), pet, null, Guid.NewGuid(), "Sahip", null,
            scheduledAtUtc, null, null, AppointmentStatus.Scheduled, null, null, false, TodayPaymentIndicator.NoPaymentRecorded, false);

    [Fact]
    public async Task Handle_Should_Fail_When_Tenant_Missing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Cannot_Be_Resolved()
    {
        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(null, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.ClinicScopeRequired");
    }

    [Fact]
    public async Task Handle_Should_Use_Clinic_Context_When_Request_Clinic_Missing()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_clinicId);

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(_clinicId);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Request_Clinic_Differs_From_Context()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _reader.Verify(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Request_Istanbul_Day_Bounds_And_Carry_Over_For_Today()
    {
        VisitTodayReadRequest? captured = null;
        _reader.Setup(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VisitTodayReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new VisitTodayReadResult([], []));

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Value!.Date.Should().Be(Today);
        captured!.DayStartUtc.Should().Be(new DateTime(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc));
        captured.DayEndUtc.Should().Be(new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Utc));
        captured.IncludeCarriedOver.Should().BeTrue();
        captured.MaxItems.Should().Be(GetVisitsTodayQueryHandler.MaxItems);
    }

    [Fact]
    public async Task Handle_Should_Not_Carry_Over_For_Other_Dates()
    {
        VisitTodayReadRequest? captured = null;
        _reader.Setup(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VisitTodayReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new VisitTodayReadResult([], []));

        var result = await CreateHandler().Handle(
            new GetVisitsTodayQuery(_clinicId, Today.AddDays(1)), CancellationToken.None);

        result.Value!.Date.Should().Be(Today.AddDays(1));
        captured!.IncludeCarriedOver.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_Order_Waiting_InProgress_Planned_Then_Completed_Descending()
    {
        var waitingLate = Visit("w2", VisitCareStatus.Waiting, T0.AddMinutes(20));
        var waitingEarly = Visit("w1", VisitCareStatus.Waiting, T0.AddMinutes(5));
        var inProgress = Visit("ip", VisitCareStatus.InProgress, T0.AddMinutes(1));
        var plannedLate = Planned("p2", T0.AddHours(3));
        var plannedEarly = Planned("p1", T0.AddHours(1));
        var completedOld = Visit("c1", VisitCareStatus.Completed, T0.AddMinutes(-60));
        var completedNew = Visit("c2", VisitCareStatus.Completed, T0.AddMinutes(-10));
        Returns([completedOld, plannedLate, waitingLate, completedNew, inProgress, plannedEarly, waitingEarly]);

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Value!.Items.Select(i => i.PetName)
            .Should().Equal("w1", "w2", "ip", "p1", "p2", "c2", "c1");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Row_Limit_Exceeded_Instead_Of_Truncating()
    {
        var rows = Enumerable.Range(0, GetVisitsTodayQueryHandler.MaxItems + 1)
            .Select(i => Visit($"p{i}", VisitCareStatus.Waiting, T0.AddSeconds(i)))
            .ToList();
        Returns(rows);

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.TodayLimitExceeded");
    }
}
