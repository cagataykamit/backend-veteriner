using Backend.Veteriner.Application.Clients.Queries.GetBalance;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>Personel ve portal bakiyesinin ortak kuralı: kalan = ücret − tahsis; bağlanmamış ödeme yalnız TRY ve kalandan düşülmez.</summary>
public sealed class ClientBalanceTotalsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    private static Charge ChargeOf(decimal amount, decimal allocated = 0)
    {
        var charge = Charge.Create(TenantId, ClinicId, ClientId, null, null, null, "Muayene", 1, amount, Guid.NewGuid(), OwnerPortalTestFixture.Now, null).Value!;
        if (allocated > 0)
            charge.ApplyAllocation(allocated).IsSuccess.Should().BeTrue();
        return charge;
    }

    private static Payment PaymentOf(decimal amount, string currency = "TRY")
        => new(TenantId, ClinicId, ClientId, null, null, null, amount, currency, PaymentMethod.Cash, OwnerPortalTestFixture.Now, null);

    [Fact]
    public void Should_Sum_Charged_Allocated_And_Outstanding()
    {
        var totals = ClientBalanceTotals.From([ChargeOf(1000m, 1000m), ChargeOf(500m, 200m)], []);

        totals.ChargedAmount.Should().Be(1500m);
        totals.AllocatedAmount.Should().Be(1200m);
        totals.OutstandingAmount.Should().Be(300m);
        totals.UnallocatedPaymentsAmount.Should().Be(0m);
    }

    [Fact]
    public void Unallocated_Payments_Should_Count_Only_Try_And_Not_Reduce_Outstanding()
    {
        var totals = ClientBalanceTotals.From([ChargeOf(400m)], [PaymentOf(150m), PaymentOf(99m, "EUR")]);

        totals.OutstandingAmount.Should().Be(400m);
        totals.UnallocatedPaymentsAmount.Should().Be(150m);
        totals.UnallocatedPaymentsCount.Should().Be(1);
    }

    [Fact]
    public void Empty_Should_Be_Zero()
    {
        var totals = ClientBalanceTotals.From([], []);

        totals.Should().Be(new ClientBalanceTotals(0m, 0m, 0m, 0m, 0));
    }
}
