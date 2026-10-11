using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetToday;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

/// <summary>Bugün satırlarının ücret/ödenen/kalan ve müşteri açık borcu alanları.</summary>
public sealed class GetVisitsTodayChargeAmountsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IVisitTodayReader> _reader = new();
    private readonly Mock<IClinicVeterinarianReader> _veterinarians = new();
    private readonly Mock<IReadRepository<Charge>> _charges = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();

    public GetVisitsTodayChargeAmountsTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(true);
    }

    private Task<Backend.Veteriner.Domain.Shared.Result<TodayDto>> RunAsync(params TodayItemDto[] items)
    {
        _reader.Setup(r => r.GetAsync(It.IsAny<VisitTodayReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VisitTodayReadResult(items, []));
        var handler = new GetVisitsTodayQueryHandler(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _reader.Object,
            _charges.Object, _permissions.Object, _veterinarians.Object, VisitHandlerTestSupport.FixedClock);
        return handler.Handle(new GetVisitsTodayQuery(_clinicId, null), CancellationToken.None);
    }

    private void SetupCharges(params ChargeAmountsRow[] rows)
        => _charges.Setup(r => r.ListAsync(It.IsAny<ChargeAmountsForClientsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());

    private static TodayItemDto VisitRow(Guid clientId, Guid visitId)
        => new(visitId, null, Guid.NewGuid(), "Pamuk", null, clientId, "Sahip", null, null,
            VisitHandlerTestSupport.FixedNowUtc, VisitCareStatus.Waiting, null, null, null, false,
            TodayPaymentIndicator.NoPaymentRecorded, false, false, null, false, new PetAlertsDto([], null));

    private static TodayItemDto PlannedRow(Guid clientId)
        => new(null, Guid.NewGuid(), Guid.NewGuid(), "Karabas", null, clientId, "Sahip", null,
            VisitHandlerTestSupport.FixedNowUtc.AddHours(1), null, null, Backend.Veteriner.Domain.Appointments.AppointmentStatus.Scheduled,
            null, null, false, TodayPaymentIndicator.NoPaymentRecorded, false, false, null, false, new PetAlertsDto([], null));

    [Fact]
    public async Task Should_Fill_Visit_And_Client_Amounts_For_Partially_Paid_And_Unpaid_Visits()
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

        var result = await RunAsync(VisitRow(clientA, paidVisit), VisitRow(clientB, unpaidVisit));

        var items = result.Value!.Items;
        var a = items.Single(i => i.VisitId == paidVisit);
        a.ChargedAmount.Should().Be(1200m);
        a.AllocatedAmount.Should().Be(400m);
        a.OutstandingAmount.Should().Be(800m);
        a.ClientOutstandingAmount.Should().Be(850m, "ziyaretsiz 50 TL ücret ve kapanmış ücret dahil müşteri toplamı");
        var b = items.Single(i => i.VisitId == unpaidVisit);
        (b.ChargedAmount, b.AllocatedAmount, b.OutstandingAmount, b.ClientOutstandingAmount).Should().Be((90m, 0m, 90m, 90m));
    }

    [Fact]
    public async Task Visit_Without_Charges_Should_Be_Zero_Not_Null_When_Permitted()
    {
        SetupCharges();

        var item = (await RunAsync(VisitRow(Guid.NewGuid(), Guid.NewGuid()))).Value!.Items.Single();

        (item.ChargedAmount, item.AllocatedAmount, item.OutstandingAmount, item.ClientOutstandingAmount)
            .Should().Be((0m, 0m, 0m, 0m));
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
    }

    [Fact]
    public async Task Without_Charges_Read_All_Amounts_Are_Null_And_No_Charge_Query_Runs()
    {
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(false);

        var item = (await RunAsync(VisitRow(Guid.NewGuid(), Guid.NewGuid()))).Value!.Items.Single();

        (item.ChargedAmount, item.AllocatedAmount, item.OutstandingAmount, item.ClientOutstandingAmount)
            .Should().Be(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null));
        _charges.Verify(r => r.ListAsync(It.IsAny<ChargeAmountsForClientsSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Query_Charges_Once_For_All_Rows()
    {
        SetupCharges();

        await RunAsync(VisitRow(Guid.NewGuid(), Guid.NewGuid()), VisitRow(Guid.NewGuid(), Guid.NewGuid()), PlannedRow(Guid.NewGuid()));

        _charges.Verify(r => r.ListAsync(It.IsAny<ChargeAmountsForClientsSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
