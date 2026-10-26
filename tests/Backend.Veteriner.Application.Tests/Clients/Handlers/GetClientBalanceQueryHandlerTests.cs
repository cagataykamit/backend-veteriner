using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Queries.GetBalance;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Visits;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class GetClientBalanceQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Charge>> _charges = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Visit>> _visits = new();
    private readonly Client _client;

    public GetClientBalanceQueryHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns(_clinicId);
        _client = new Client(_tenantId, "Ali Veli", "05321111111");
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_client);
        SetupCharges();
        SetupPayments();
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _visits.Setup(r => r.ListAsync(It.IsAny<VisitsByIdsSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private GetClientBalanceQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _scopeResolver.Object, _clients.Object, _charges.Object,
            _payments.Object, _pets.Object, _visits.Object);

    private void SetupCharges(params Charge[] charges)
        => _charges.Setup(r => r.ListAsync(It.IsAny<ChargesForClientBalanceSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(charges.ToList());

    private void SetupPayments(params Payment[] payments)
        => _payments.Setup(r => r.ListAsync(It.IsAny<UnallocatedPaymentsForClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments.ToList());

    private Charge NewCharge(decimal amount, decimal allocated = 0m, Guid? petId = null, Guid? visitId = null, string description = "Muayene")
    {
        var charge = Charge.Create(
                _tenantId, _clinicId, _client.Id, petId, visitId, null, description, 1m, amount,
                Guid.NewGuid(), DateTime.UtcNow, null)
            .Value!;
        if (allocated > 0)
            charge.ApplyAllocation(allocated).IsSuccess.Should().BeTrue();
        return charge;
    }

    private Payment NewPayment(decimal amount, string currency = "TRY")
        => new(_tenantId, _clinicId, _client.Id, null, null, null, amount, currency, PaymentMethod.Cash,
            DateTime.UtcNow.AddDays(-30), "Eski kayit");

    [Fact]
    public async Task Handle_NoCharges_Should_Return_Zero_Balance_Not_404()
    {
        var result = await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Currency.Should().Be("TRY");
        dto.ChargedAmount.Should().Be(0m);
        dto.AllocatedAmount.Should().Be(0m);
        dto.OutstandingAmount.Should().Be(0m);
        dto.OpenChargeCount.Should().Be(0);
        dto.Pets.Should().BeEmpty();
        dto.UnallocatedPayments.PaymentCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Charge_1000_Paid_400_Should_Report_600_Outstanding()
    {
        SetupCharges(NewCharge(1000m, 400m));

        var dto = (await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None)).Value!;

        dto.ChargedAmount.Should().Be(1000m);
        dto.AllocatedAmount.Should().Be(400m);
        dto.OutstandingAmount.Should().Be(600m);
        dto.OpenChargeCount.Should().Be(1);
        dto.ClinicId.Should().Be(_clinicId);
    }

    [Fact]
    public async Task Handle_Closed_Charge_Counts_In_Totals_But_Is_Not_Listed()
    {
        var petId = Guid.NewGuid();
        SetupCharges(NewCharge(500m, 500m, petId), NewCharge(300m, 100m, petId, description: "Asi"));

        var dto = (await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None)).Value!;

        dto.ChargedAmount.Should().Be(800m);
        dto.AllocatedAmount.Should().Be(600m);
        dto.OutstandingAmount.Should().Be(200m);
        dto.OpenChargeCount.Should().Be(1);
        var charges = dto.Pets.Single().Visits.Single().Charges;
        charges.Should().ContainSingle().Which.Description.Should().Be("Asi");
    }

    [Fact]
    public async Task Handle_Should_Group_Open_Charges_By_Pet_And_Visit_With_Null_Groups()
    {
        var pet1 = Guid.NewGuid();
        var pet2 = Guid.NewGuid();
        var visitA = new Visit(_tenantId, _clinicId, pet1, null, null, Guid.NewGuid(), new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        var visitB = new Visit(_tenantId, _clinicId, pet1, null, null, Guid.NewGuid(), new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));
        SetupCharges(
            NewCharge(100m, 0m, pet1, visitA.Id),
            NewCharge(250m, 50m, pet1, visitB.Id),
            NewCharge(40m, 0m, pet1, null),
            NewCharge(900m, 0m, pet2, null),
            NewCharge(60m, 0m, null, null));
        _visits.Setup(r => r.ListAsync(It.IsAny<VisitsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([visitA, visitB]);

        var dto = (await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None)).Value!;

        dto.OutstandingAmount.Should().Be(1300m);
        dto.OpenChargeCount.Should().Be(5);
        dto.Pets.Select(p => p.OutstandingAmount).Should().Equal(900m, 340m, 60m);
        dto.Pets[0].PetId.Should().Be(pet2);
        var pet1Group = dto.Pets[1];
        pet1Group.PetId.Should().Be(pet1);
        pet1Group.Visits.Select(v => v.VisitId).Should().Equal(visitB.Id, visitA.Id, null);
        pet1Group.Visits.Select(v => v.OutstandingAmount).Should().Equal(200m, 100m, 40m);
        pet1Group.Visits[0].VisitArrivedAtUtc.Should().Be(visitB.ArrivedAtUtc);
        pet1Group.Visits[2].VisitArrivedAtUtc.Should().BeNull();
        dto.Pets[2].PetId.Should().BeNull();
        dto.Pets[2].PetName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Legacy_Unallocated_Payments_Are_Separate_And_Create_No_Debt_Or_Netting()
    {
        SetupCharges(NewCharge(1000m, 400m));
        SetupPayments(NewPayment(120m), NewPayment(30m), NewPayment(75m, "USD"));

        var dto = (await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None)).Value!;

        dto.OutstandingAmount.Should().Be(600m);
        dto.UnallocatedPayments.TotalAmount.Should().Be(150m);
        dto.UnallocatedPayments.PaymentCount.Should().Be(2);
        dto.UnallocatedPayments.HasOtherCurrency.Should().BeTrue();
        dto.UnallocatedPayments.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_Unallocated_Items_Are_Capped_But_Count_And_Total_Cover_All()
    {
        SetupPayments(Enumerable.Range(0, 60).Select(_ => NewPayment(10m)).ToArray());

        var dto = (await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None)).Value!;

        dto.UnallocatedPayments.Items.Should().HaveCount(50);
        dto.UnallocatedPayments.PaymentCount.Should().Be(60);
        dto.UnallocatedPayments.TotalAmount.Should().Be(600m);
        dto.UnallocatedPayments.HasOtherCurrency.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Unknown_Client_Should_Return_Clients_NotFound()
    {
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(new GetClientBalanceQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Handle_Missing_Tenant_Should_Return_ContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Clinic_Scope_Failure_Should_Be_Propagated()
    {
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Without_Active_Clinic_Should_Return_Null_ClinicId()
    {
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var dto = (await CreateHandler().Handle(new GetClientBalanceQuery(_client.Id), CancellationToken.None)).Value!;

        dto.ClinicId.Should().BeNull();
    }
}
