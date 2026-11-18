using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetOpenItemVisits;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Visits;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

/// <summary>Açık iş listesi: Bugün özetiyle aynı kapsam, izin ve pencere; kural okuyucudadır.</summary>
public sealed class GetOpenItemVisitsQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IVisitOpenItemsReader> _openItems = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IClinicVeterinarianReader> _veterinarians = new();

    public GetOpenItemVisitsQueryHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(true);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Examinations.Read)).Returns(true);
        _veterinarians.Setup(v => v.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string?>());
        ReturnsRows(0);
    }

    private GetOpenItemVisitsQueryHandler CreateHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _openItems.Object,
            _permissions.Object, _veterinarians.Object, VisitHandlerTestSupport.FixedClock);

    private ValueTask<Backend.Veteriner.Domain.Shared.Result<OpenItemVisitsDto>> RunAsync(
        OpenItemVisitKind kind, Guid? clinicId = null)
        => CreateHandler().Handle(new GetOpenItemVisitsQuery(clinicId ?? _clinicId, kind), CancellationToken.None);

    private void ReturnsRows(int total, params OpenItemVisitRow[] rows)
        => _openItems.Setup(r => r.ListVisitsAsync(
                It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<OpenItemVisitKind>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenItemVisitsReadResult(total, rows));

    private static OpenItemVisitRow Row(Guid? veterinarianId = null)
        => new(Guid.NewGuid(), VisitHandlerTestSupport.FixedNowUtc.AddDays(-2), VisitCareStatus.Waiting, Guid.NewGuid(), "Pamuk", "Kedi",
            Guid.NewGuid(), "Sahip", veterinarianId, Guid.NewGuid());

    [Fact]
    public async Task Handle_Should_Fail_When_Request_Clinic_Differs_From_Context()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await RunAsync(OpenItemVisitKind.Incomplete);

        result.Error.Code.Should().Be("Visits.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        _scopeResolver.SetupAccessDenied();

        var result = await RunAsync(OpenItemVisitKind.Incomplete);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _openItems.Verify(r => r.ListVisitsAsync(
            It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<OpenItemVisitKind>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Forbid_Uncharged_List_Without_Charges_Read()
    {
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(false);

        var result = await RunAsync(OpenItemVisitKind.Uncharged);

        result.Error.Code.Should().Be("Visits.OpenItemsForbidden");
    }

    [Fact]
    public async Task Handle_Should_Reject_Undefined_Kind()
    {
        var result = await RunAsync((OpenItemVisitKind)9);

        result.Error.Code.Should().Be("Visits.Validation");
    }

    [Fact]
    public async Task Handle_Should_Use_Today_Summary_Window_And_Visible_Sources()
    {
        OpenItemsSummaryRequest? captured = null;
        _openItems.Setup(r => r.ListVisitsAsync(
                It.IsAny<OpenItemsSummaryRequest>(), OpenItemVisitKind.Incomplete, GetOpenItemVisitsQueryHandler.MaxItems, It.IsAny<CancellationToken>()))
            .Callback((OpenItemsSummaryRequest r, OpenItemVisitKind _, int _, CancellationToken _) => captured = r)
            .ReturnsAsync(new OpenItemVisitsReadResult(0, []));

        var result = await RunAsync(OpenItemVisitKind.Incomplete);

        result.IsSuccess.Should().BeTrue();
        var expectedScope = new OpenItemsScope(_tenantId, _clinicId, OpenItemSources.Charges | OpenItemSources.Examinations);
        captured.Should().Be(OpenItemsSummaryRequest.ForToday(expectedScope, VisitHandlerTestSupport.FixedNowUtc));
        // FixedNowUtc = 2026-10-10 09:00 UTC → İstanbul günü 2026-10-10; ücret yok penceresi 30 gün geriden başlar.
        captured!.IncompleteBeforeUtc.Should().Be(OperationDayBounds.ForLocalDate(new DateOnly(2026, 10, 10)).DayStartUtc);
        captured.UnchargedFromUtc.Should().Be(OperationDayBounds.ForLocalDate(new DateOnly(2026, 9, 10)).DayStartUtc);
    }

    [Fact]
    public async Task Handle_Should_Fill_Veterinarian_Names_And_Work_Count_For_Uncharged()
    {
        var veterinarianId = Guid.NewGuid();
        var row = Row(veterinarianId);
        var unassigned = Row();
        ReturnsRows(5, row, unassigned);
        _veterinarians.Setup(v => v.GetNamesAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == veterinarianId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string?> { [veterinarianId] = "Dr. Ayşe" });
        _openItems.Setup(r => r.GetForVisitsAsync(It.IsAny<OpenItemsScope>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, VisitOpenItems>
            {
                [row.VisitId] = new(VisitChargeIndicator.ChargeMissing, 3, 0m, 0m, null),
                [unassigned.VisitId] = new(VisitChargeIndicator.ChargeMissing, 1, 0m, 0m, null),
            });

        var result = await RunAsync(OpenItemVisitKind.Uncharged);

        result.Value!.Kind.Should().Be(OpenItemVisitKind.Uncharged);
        result.Value.TotalCount.Should().Be(5);
        var first = result.Value.Items[0];
        (first.VisitId, first.ResponsibleVeterinarianName, first.ChargeableWorkCount, first.LatestExaminationId)
            .Should().Be((row.VisitId, "Dr. Ayşe", (int?)3, row.LatestExaminationId));
        result.Value.Items[1].ResponsibleVeterinarianName.Should().BeNull();
        result.Value.Items[1].ChargeableWorkCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Should_Not_Read_Work_Counts_For_Incomplete_List()
    {
        ReturnsRows(1, Row());

        var result = await RunAsync(OpenItemVisitKind.Incomplete);

        result.Value!.Items.Single().ChargeableWorkCount.Should().BeNull();
        _openItems.Verify(r => r.GetForVisitsAsync(
            It.IsAny<OpenItemsScope>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        _veterinarians.Verify(v => v.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
