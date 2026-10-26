using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.SalesPartners;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.SalesPartners;

/// <summary>ADR-037 madde 7: ortak başına rapor (deneme/ücretli müşteri, ciro, hak edilen/ödenen/bekleyen/geri alınacak komisyon).</summary>
public sealed class SalesPartnerSummaryDtoTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void From_Should_Sum_Revenue_And_Commission_By_Status()
    {
        var partner = SalesPartner.Create("Ortak", null, null, null, Now);
        var code = DiscountCode.Create(partner.Id, "ISIK10", 10m, 20m, null, null, null, Now).Value!;
        DiscountRedemption Redeem() => DiscountRedemption.Redeem(Guid.NewGuid(), null, code, partner, 0, Now).Value!;

        var trial = Redeem();
        var paying = Redeem();
        var accrued = paying.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 100_000, 10_000, Now, Now)!;
        var paid = paying.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 100_000, 10_000, Now.AddMonths(1), Now.AddMonths(1))!;
        var refundedAfterPay = paying.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 100_000, 10_000, Now.AddMonths(2), Now.AddMonths(2))!;
        paying.MarkCommissionPaid(paid.Id, Now, null);
        paying.MarkCommissionPaid(refundedAfterPay.Id, Now, null);
        paying.CancelCollection(refundedAfterPay.Id, "iade", Now);

        var refundedOnly = Redeem();
        var onlyCollection = refundedOnly.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 100_000, 10_000, Now, Now)!;
        refundedOnly.CancelCollection(onlyCollection.Id, "iade", Now);

        var summary = SalesPartnerSummaryDto.From([trial, paying, refundedOnly]);

        summary.RedemptionCount.Should().Be(3);
        summary.PaidCustomerCount.Should().Be(1);
        summary.TrialCustomerCount.Should().Be(2, "etkin tahsilatı olmayan (iade edilen dahil) deneme sayılır");
        summary.RevenueNetMinor.Should().Be(180_000);
        summary.CommissionEarnedMinor.Should().Be(36_000);
        summary.CommissionPaidMinor.Should().Be(18_000);
        summary.CommissionPendingMinor.Should().Be(accrued.CommissionAmountMinor);
        summary.CommissionClawbackMinor.Should().Be(18_000);
    }
}
