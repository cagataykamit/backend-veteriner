using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetToday;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Visits;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

/// <summary>Bugün satırlarının ücret/ödenen/kalan, müşteri açık borcu ve açık iş alanları (hesap kuralı <see cref="IVisitOpenItemsReader"/>'da).</summary>
public sealed class GetVisitsTodayChargeAmountsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IVisitTodayReader> _reader = new();
    private readonly Mock<IVisitOpenItemsReader> _openItems = new();
    private readonly Mock<IClinicVeterinarianReader> _veterinarians = new();
    private readonly Mock<IReadRepository<Charge>> _charges = new();
    private readonly Mock<IReadRepository<ChargeSuggestion>> _chargeSuggestions = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();

    public GetVisitsTodayChargeAmountsTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(true);
        _chargeSuggestions
            .Setup(r => r.ListAsync(It.IsAny<PendingChargeSuggestionVisitIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        SetupOpenItems(new Dictionary<Guid, VisitOpenItems>());
        _openItems.Setup(r => r.GetSummaryAsync(It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenItemsSummaryDto(1, 2, 30m, 4, 5));
    }

    private ValueTask<Backend.Veteriner.Domain.Shared.Result<TodayDto>> RunAsync(params TodayItemDto[] items)
        => RunAsync(voided: false, items);

    private ValueTask<Backend.Veteriner.Domain.Shared.Result<TodayDto>> RunAsync(bool voided, params TodayItemDto[] items)
    {
        _reader.Setup(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VisitTodayReadResult(items, []));
        var handler = new GetVisitsTodayQueryHandler(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _reader.Object, _openItems.Object,
            _charges.Object, _chargeSuggestions.Object, _permissions.Object, _veterinarians.Object, VisitHandlerTestSupport.FixedClock);
        return handler.Handle(new GetVisitsTodayQuery(_clinicId, null, Voided: voided), CancellationToken.None);
    }

    private void SetupCharges(params ChargeAmountsRow[] rows)
        => _charges.Setup(r => r.ListAsync(It.IsAny<ChargeAmountsForClientsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());

    private void SetupOpenItems(IReadOnlyDictionary<Guid, VisitOpenItems> byVisit)
        => _openItems.Setup(r => r.GetForVisitsAsync(
                It.IsAny<OpenItemsScope>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(byVisit);

    private static TodayItemDto VisitRow(Guid clientId, Guid visitId)
        => new(visitId, null, Guid.NewGuid(), "Pamuk", null, clientId, "Sahip", null, null,
            VisitHandlerTestSupport.FixedNowUtc, VisitCareStatus.Waiting, null, null, null, false,
            TodayPaymentIndicator.NoPaymentRecorded, false, false, null, false, new PetAlertsDto([], null));

    private static TodayItemDto PlannedRow(Guid clientId)
        => new(null, Guid.NewGuid(), Guid.NewGuid(), "Karabas", null, clientId, "Sahip", null,
            VisitHandlerTestSupport.FixedNowUtc.AddHours(1), null, null, Backend.Veteriner.Domain.Appointments.AppointmentStatus.Scheduled,
            null, null, false, TodayPaymentIndicator.NoPaymentRecorded, false, false, null, false, new PetAlertsDto([], null));

    [Fact]
    public async Task Should_Fill_Visit_Amounts_And_Open_Items_From_Reader_And_Client_Outstanding_From_Charges()
    {
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var paidVisit = Guid.NewGuid();
        var unpaidVisit = Guid.NewGuid();
        SetupCharges(
            new ChargeAmountsRow(clientA, paidVisit, 1000m, 400m),
            new ChargeAmountsRow(clientA, paidVisit, 200m, 0m),
            new ChargeAmountsRow(clientA, null, 50m, 0m),
            new ChargeAmountsRow(clientA, Guid.NewGuid(), 300m, 300m),
            new ChargeAmountsRow(clientB, unpaidVisit, 90m, 0m));
        SetupOpenItems(new Dictionary<Guid, VisitOpenItems>
        {
            [paidVisit] = new(VisitChargeIndicator.PartiallyPaid, 3, 1200m, 400m, 2),
            [unpaidVisit] = new(VisitChargeIndicator.PaymentPending, 1, 90m, 0m, 0),
        });

        var result = await RunAsync(VisitRow(clientA, paidVisit), VisitRow(clientB, unpaidVisit));

        var items = result.Value!.Items;
        var a = items.Single(i => i.VisitId == paidVisit);
        (a.ChargedAmount, a.AllocatedAmount, a.OutstandingAmount).Should().Be((1200m, 400m, 800m));
        a.ClientOutstandingAmount.Should().Be(850m, "ziyaretsiz 50 TL ücret ve kapanmış ücret dahil müşteri toplamı");
        (a.ChargeIndicator, a.ChargeableWorkCount, a.AwaitingLabCount)
            .Should().Be((VisitChargeIndicator.PartiallyPaid, 3, 2));
        var b = items.Single(i => i.VisitId == unpaidVisit);
        (b.ChargedAmount, b.AllocatedAmount, b.OutstandingAmount, b.ClientOutstandingAmount).Should().Be((90m, 0m, 90m, 90m));
        b.ChargeIndicator.Should().Be(VisitChargeIndicator.PaymentPending);
    }

    [Fact]
    public async Task Planned_Row_Without_Visit_Should_Only_Have_Client_Outstanding()
    {
        var clientId = Guid.NewGuid();
        SetupCharges(new ChargeAmountsRow(clientId, null, 120m, 20m));

        var item = (await RunAsync(PlannedRow(clientId))).Value!.Items.Single();

        item.ChargedAmount.Should().BeNull();
        item.AllocatedAmount.Should().BeNull();
        item.OutstandingAmount.Should().BeNull();
        item.ClientOutstandingAmount.Should().Be(100m);
        (item.ChargeIndicator, item.ChargeableWorkCount, item.AwaitingLabCount)
            .Should().Be(((VisitChargeIndicator?)null, (int?)null, (int?)null));
        _openItems.Verify(r => r.GetForVisitsAsync(
            It.IsAny<OpenItemsScope>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_Charges_Read_Charge_Fields_Are_Null_And_No_Charge_Query_Runs_But_Lab_Count_Stays()
    {
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(false);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.LabResults.Read)).Returns(true);
        var visitId = Guid.NewGuid();
        SetupOpenItems(new Dictionary<Guid, VisitOpenItems>
        {
            [visitId] = new(null, null, null, null, 2),
        });

        var item = (await RunAsync(VisitRow(Guid.NewGuid(), visitId))).Value!.Items.Single();

        (item.ChargedAmount, item.AllocatedAmount, item.OutstandingAmount, item.ClientOutstandingAmount, item.ChargeIndicator, item.ChargeableWorkCount)
            .Should().Be(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null, (VisitChargeIndicator?)null, (int?)null));
        item.AwaitingLabCount.Should().Be(2);
        item.PendingChargeCount.Should().BeNull();
        _charges.Verify(r => r.ListAsync(It.IsAny<ChargeAmountsForClientsSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _chargeSuggestions.Verify(r => r.ListAsync(It.IsAny<PendingChargeSuggestionVisitIdsSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _openItems.Verify(r => r.GetForVisitsAsync(
            It.Is<OpenItemsScope>(s => s.Visible == OpenItemSources.LabResults),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_Any_Read_Permission_Reader_For_Rows_Is_Not_Called_And_Rows_Stay_Null()
    {
        _permissions.Setup(p => p.HasPermission(It.IsAny<string>())).Returns(false);

        var item = (await RunAsync(VisitRow(Guid.NewGuid(), Guid.NewGuid()))).Value!.Items.Single();

        (item.ChargeIndicator, item.ChargeableWorkCount, item.AwaitingLabCount, item.ChargedAmount)
            .Should().Be(((VisitChargeIndicator?)null, (int?)null, (int?)null, (decimal?)null));
        _openItems.Verify(r => r.GetForVisitsAsync(
            It.IsAny<OpenItemsScope>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Count_Pending_Charge_Suggestions_Per_Visit()
    {
        var withPending = Guid.NewGuid();
        var withoutPending = Guid.NewGuid();
        SetupCharges();
        _chargeSuggestions
            .Setup(r => r.ListAsync(It.IsAny<PendingChargeSuggestionVisitIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { withPending, withPending });

        var items = (await RunAsync(VisitRow(Guid.NewGuid(), withPending), VisitRow(Guid.NewGuid(), withoutPending))).Value!.Items;

        items.Single(i => i.VisitId == withPending).PendingChargeCount.Should().Be(2);
        items.Single(i => i.VisitId == withoutPending).PendingChargeCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_Query_Charges_And_Open_Items_Once_For_All_Rows()
    {
        SetupCharges();

        await RunAsync(VisitRow(Guid.NewGuid(), Guid.NewGuid()), VisitRow(Guid.NewGuid(), Guid.NewGuid()), PlannedRow(Guid.NewGuid()));

        _charges.Verify(r => r.ListAsync(It.IsAny<ChargeAmountsForClientsSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _openItems.Verify(r => r.GetForVisitsAsync(
            It.IsAny<OpenItemsScope>(),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Summary_Should_Be_Requested_With_Thirty_Day_Window_And_Start_Of_Today()
    {
        SetupCharges();

        var result = await RunAsync(VisitRow(Guid.NewGuid(), Guid.NewGuid()));

        result.Value!.OpenItems.Should().Be(new OpenItemsSummaryDto(1, 2, 30m, 4, 5));
        // FixedNowUtc = 2026-10-10 09:00 UTC → İstanbul günü 2026-10-10 (başlangıç 2026-10-09 21:00 UTC), pencere 30 gün öncesi.
        _openItems.Verify(r => r.GetSummaryAsync(
            It.Is<OpenItemsSummaryRequest>(q =>
                q.UnchargedFromUtc == new DateTime(2026, 9, 9, 21, 0, 0, DateTimeKind.Utc)
                && q.UnchargedToUtc == VisitHandlerTestSupport.FixedNowUtc
                && q.IncompleteBeforeUtc == new DateTime(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc)
                && q.Scope.ClinicId == _clinicId
                && q.Scope.TenantId == _tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Voided_View_Should_Not_Compute_Summary()
    {
        SetupCharges();

        var result = await RunAsync(voided: true, VisitRow(Guid.NewGuid(), Guid.NewGuid()));

        result.Value!.OpenItems.Should().BeNull();
        _openItems.Verify(r => r.GetSummaryAsync(It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
