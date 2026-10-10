using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
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
    private readonly Mock<IClinicVeterinarianReader> _veterinarians = new();

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
            _veterinarians.Object,
            VisitHandlerTestSupport.FixedClock);

    private void Returns(IReadOnlyList<TodayItemDto> items)
        => _reader.Setup(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VisitTodayReadResult(items, []));

    private static TodayItemDto Visit(
        string pet, VisitCareStatus status, DateTime arrivedAtUtc, DateTime? scheduledAtUtc = null, bool isUrgent = false)
        => new(Guid.NewGuid(), null, Guid.NewGuid(), pet, null, Guid.NewGuid(), "Sahip", null,
            scheduledAtUtc, arrivedAtUtc, status, null, null, null, false, TodayPaymentIndicator.NoPaymentRecorded, false, false, null, isUrgent);

    private static TodayItemDto Planned(string pet, DateTime scheduledAtUtc)
        => new(null, Guid.NewGuid(), Guid.NewGuid(), pet, null, Guid.NewGuid(), "Sahip", null,
            scheduledAtUtc, null, null, AppointmentStatus.Scheduled, null, null, false, TodayPaymentIndicator.NoPaymentRecorded, false, false, null, false);

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
    public async Task Handle_Should_Put_Urgent_First_Within_Waiting_And_InProgress_Only()
    {
        var waitingEarly = Visit("w1", VisitCareStatus.Waiting, T0.AddMinutes(5));
        var waitingUrgentLate = Visit("w2u", VisitCareStatus.Waiting, T0.AddMinutes(20), isUrgent: true);
        var waitingUrgentLater = Visit("w3u", VisitCareStatus.Waiting, T0.AddMinutes(30), isUrgent: true);
        var inProgress = Visit("ip", VisitCareStatus.InProgress, T0.AddMinutes(1));
        var inProgressUrgent = Visit("ipu", VisitCareStatus.InProgress, T0.AddMinutes(9), isUrgent: true);
        var completedOld = Visit("c1", VisitCareStatus.Completed, T0.AddMinutes(-60), isUrgent: true);
        var completedNew = Visit("c2", VisitCareStatus.Completed, T0.AddMinutes(-10));
        Returns([completedOld, waitingEarly, inProgress, waitingUrgentLater, completedNew, inProgressUrgent, waitingUrgentLate]);

        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.Value!.Items.Select(i => i.PetName)
            .Should().Equal("w2u", "w3u", "w1", "ipu", "ip", "c2", "c1");
    }

    [Fact]
    public async Task Handle_Should_Pass_Responsible_Veterinarian_To_Reader_When_Clinic_Veterinarian()
    {
        var vetId = Guid.NewGuid();
        _veterinarians.Setup(v => v.IsClinicVeterinarianAsync(vetId, _tenantId, _clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(
            new GetVisitsTodayQuery(_clinicId, null, ResponsibleVeterinarianUserId: vetId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reader.Verify(r => r.GetAsync(
            It.Is<VisitTodayReadRequest>(q => q.ResponsibleVeterinarianUserId == vetId && q.ClinicId == _clinicId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_Validation_When_Responsible_User_Is_Not_Clinic_Veterinarian()
    {
        var result = await CreateHandler().Handle(
            new GetVisitsTodayQuery(_clinicId, null, ResponsibleVeterinarianUserId: Guid.NewGuid()),
            CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
        _reader.Verify(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Not_Check_Veterinarian_Rule_Without_Filter()
    {
        var result = await CreateHandler().Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _veterinarians.Verify(v => v.IsClinicVeterinarianAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task Handle_Should_Pass_Voided_Flag_And_Order_Voided_By_Arrival_Descending()
    {
        var older = Visit("v1", VisitCareStatus.Completed, T0.AddMinutes(-30));
        var newer = Visit("v2", VisitCareStatus.Waiting, T0.AddMinutes(-5));
        Returns([older, newer]);

        var result = await CreateHandler().Handle(
            new GetVisitsTodayQuery(_clinicId, null, Voided: true), CancellationToken.None);

        result.Value!.Items.Select(i => i.PetName).Should().Equal("v2", "v1");
        _reader.Verify(r => r.GetAsync(
            It.Is<VisitTodayReadRequest>(q => q.OnlyVoided), It.IsAny<CancellationToken>()), Times.Once);
    }
}
