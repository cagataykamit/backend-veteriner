using Backend.Veteriner.Domain.SalesPartners;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.SalesPartners;

/// <summary>
/// ADR-037: kullanım (tek tip geçersiz, kendi kodunu kullanma), 12 ay penceresi (aylık/yıllık), komisyon hesabı,
/// iade/iptalde geri alma, ödendi işareti ve fiyat sırası (plan → yıllık indirim → kod indirimi → net).
/// </summary>
public sealed class DiscountRedemptionTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (SalesPartner Partner, DiscountCode Code) PartnerWithCode(
        decimal discount = 10m, decimal commission = 20m, string? email = "ortak@example.com", Guid? linkedTenant = null)
    {
        var partner = SalesPartner.Create("Ali Ortak", email, null, linkedTenant, Now);
        var code = DiscountCode.Create(partner.Id, "ISIK10", discount, commission, null, null, null, Now).Value!;
        return (partner, code);
    }

    private static DiscountRedemption Redeem(decimal discount = 10m, decimal commission = 20m)
    {
        var (partner, code) = PartnerWithCode(discount, commission);
        return DiscountRedemption.Redeem(TenantId, "klinik@example.com", code, partner, 0, Now).Value!;
    }

    [Fact]
    public void Redeem_Should_Copy_Percents_And_Leave_Window_Open_Until_First_Collection()
    {
        var redemption = Redeem();

        redemption.TenantId.Should().Be(TenantId);
        redemption.Code.Should().Be("ISIK10");
        redemption.DiscountPercent.Should().Be(10m);
        redemption.CommissionPercent.Should().Be(20m);
        redemption.WindowStartsAtUtc.Should().BeNull();
        redemption.HasActiveCollection.Should().BeFalse();
    }

    [Fact]
    public void Redeem_Should_Return_Uniform_Invalid_For_Inactive_Code_Inactive_Partner_Limit_And_Self_Use()
    {
        var (partner, code) = PartnerWithCode(linkedTenant: TenantId);

        DiscountRedemption.Redeem(TenantId, "baska@example.com", code, partner, 0, Now)
            .Error.Should().Be(DiscountRedemption.InvalidCode, "ortağın kendi kiracısı");
        DiscountRedemption.Redeem(Guid.NewGuid(), " Ortak@Example.com ", code, partner, 0, Now)
            .Error.Should().Be(DiscountRedemption.InvalidCode, "ortağın kendi e-postası");

        var (other, otherCode) = PartnerWithCode();
        otherCode.Deactivate(Now);
        DiscountRedemption.Redeem(TenantId, null, otherCode, other, 0, Now).Error.Should().Be(DiscountRedemption.InvalidCode);

        var (inactive, inactiveCode) = PartnerWithCode();
        inactive.Update(inactive.Name, inactive.Email, null, null, isActive: false, Now);
        DiscountRedemption.Redeem(TenantId, null, inactiveCode, inactive, 0, Now).Error.Should().Be(DiscountRedemption.InvalidCode);

        var limitedPartner = SalesPartner.Create("Limitli", null, null, null, Now);
        var limited = DiscountCode.Create(limitedPartner.Id, "LIMIT1", 10, 20, null, null, 1, Now).Value!;
        DiscountRedemption.Redeem(TenantId, null, limited, limitedPartner, 1, Now).Error.Should().Be(DiscountRedemption.InvalidCode);
    }

    [Fact]
    public void Monthly_Should_Discount_And_Commission_Within_12_Months_From_First_Collection_Only()
    {
        var redemption = Redeem();
        var first = Now.AddDays(14);

        // Aylık yenileme: 12 tahsilat pencere içinde, 13. değil.
        for (var month = 0; month < 12; month++)
        {
            var at = first.AddMonths(month);
            var discount = redemption.CalculateDiscountMinor(99_900, at, BillingPeriodKind.Monthly);
            discount.Should().Be(9_990);
            redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 99_900, discount, at, at).Should().NotBeNull();
        }

        redemption.WindowStartsAtUtc.Should().Be(first);
        redemption.WindowEndsAtUtc.Should().Be(first.AddMonths(12));

        var thirteenth = first.AddMonths(12);
        redemption.CalculateDiscountMinor(99_900, thirteenth, BillingPeriodKind.Monthly).Should().Be(0);
        redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 99_900, 0, thirteenth, thirteenth).Should().BeNull();
        redemption.Collections.Should().HaveCount(12);
        redemption.Collections.Should().OnlyContain(c => c.NetAmountMinor == 89_910 && c.CommissionAmountMinor == 17_982);
    }

    [Fact]
    public void RecordCollection_Should_Be_Idempotent_Per_Checkout_Session()
    {
        var redemption = Redeem();
        var session = Guid.NewGuid();

        redemption.RecordCollection(session, BillingPeriodKind.Monthly, 99_900, 9_990, Now, Now).Should().NotBeNull();
        redemption.RecordCollection(session, BillingPeriodKind.Monthly, 99_900, 9_990, Now, Now).Should().BeNull();
        redemption.Collections.Should().HaveCount(1);
    }

    [Fact]
    public void Yearly_Should_Apply_Once_And_Commission_Once()
    {
        var redemption = Redeem();

        var price = SubscriptionPricing.Calculate(99_900, BillingPeriodKind.Yearly, redemption, Now);
        price.ListAmountMinor.Should().Be(1_198_800);
        price.PeriodDiscountMinor.Should().Be(199_800);
        price.AmountBeforeCodeMinor.Should().Be(999_000);
        price.CodeDiscountMinor.Should().Be(99_900);
        price.NetAmountMinor.Should().Be(899_100);

        var collection = redemption.RecordCollection(
            Guid.NewGuid(), BillingPeriodKind.Yearly, price.AmountBeforeCodeMinor, price.CodeDiscountMinor, Now, Now)!;
        collection.CommissionAmountMinor.Should().Be(179_820, "komisyon tüm indirimler sonrası net üzerinden");

        // Pencere içinde ikinci yıllık tahsilat indirimsiz ve komisyonsuz.
        var later = Now.AddMonths(6);
        redemption.IsEligible(later, BillingPeriodKind.Yearly).Should().BeFalse();
        SubscriptionPricing.Calculate(99_900, BillingPeriodKind.Yearly, redemption, later).CodeDiscountMinor.Should().Be(0);
        redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Yearly, 999_000, 0, later, later).Should().BeNull();
    }

    [Fact]
    public void Yearly_Should_Become_Eligible_Again_When_The_Only_Collection_Is_Refunded_Within_Window()
    {
        var redemption = Redeem();
        var collection = redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Yearly, 999_000, 99_900, Now, Now)!;

        redemption.CancelCollection(collection.Id, "iade", Now.AddDays(1)).IsSuccess.Should().BeTrue();

        redemption.IsEligible(Now.AddDays(2), BillingPeriodKind.Yearly).Should().BeTrue();
        redemption.IsEligible(Now.AddMonths(12), BillingPeriodKind.Yearly).Should().BeFalse("pencere ilk tahsilattan başlar, iade sıfırlamaz");
    }

    [Theory]
    [InlineData(BillingPeriodKind.Monthly, false, 99_900, 0, 0, 99_900)]
    [InlineData(BillingPeriodKind.Monthly, true, 99_900, 0, 9_990, 89_910)]
    [InlineData(BillingPeriodKind.Yearly, false, 1_198_800, 199_800, 0, 999_000)]
    [InlineData(BillingPeriodKind.Yearly, true, 1_198_800, 199_800, 99_900, 899_100)]
    public void Pricing_Order_Should_Be_Plan_Then_Period_Then_Code(
        BillingPeriodKind period, bool withCode, long list, long periodDiscount, long codeDiscount, long net)
    {
        var price = SubscriptionPricing.Calculate(99_900, period, withCode ? Redeem() : null, Now);

        price.ListAmountMinor.Should().Be(list);
        price.PeriodDiscountMinor.Should().Be(periodDiscount);
        price.CodeDiscountMinor.Should().Be(codeDiscount);
        price.NetAmountMinor.Should().Be(net);
    }

    [Fact]
    public void Pricing_ForAmount_Should_Apply_Only_Code_Discount_And_Keep_At_Least_One_Kurus()
    {
        SubscriptionPricing.ForAmount(50_000, BillingPeriodKind.Monthly, Redeem(), Now).NetAmountMinor.Should().Be(45_000);
        SubscriptionPricing.ForAmount(1, BillingPeriodKind.Monthly, Redeem(discount: 99m), Now).NetAmountMinor.Should().Be(1);
    }

    [Fact]
    public void Commission_Should_Round_Away_From_Zero()
    {
        var redemption = Redeem(discount: 0m, commission: 15m);

        // 333 × %15 = 49,95 → 50
        redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 333, 0, Now, Now)!
            .CommissionAmountMinor.Should().Be(50);
    }

    [Fact]
    public void Cancel_Should_Reverse_Commission_And_Paid_Cancel_Should_Be_Clawback()
    {
        var redemption = Redeem();
        var accrued = redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 99_900, 9_990, Now, Now)!;
        var paid = redemption.RecordCollection(Guid.NewGuid(), BillingPeriodKind.Monthly, 99_900, 9_990, Now.AddMonths(1), Now.AddMonths(1))!;

        redemption.MarkCommissionPaid(paid.Id, Now.AddMonths(1), "Havale").IsSuccess.Should().BeTrue();
        paid.Status.Should().Be(CommissionStatus.Paid);
        paid.PaidNote.Should().Be("Havale");

        redemption.CancelCollection(accrued.Id, "  ", Now).Error.Code.Should().Be("DiscountRedemptions.Validation");
        redemption.CancelCollection(accrued.Id, "iyzico iadesi", Now).IsSuccess.Should().BeTrue();
        accrued.Status.Should().Be(CommissionStatus.Cancelled);
        accrued.IsActive.Should().BeFalse();
        accrued.IsClawback.Should().BeFalse();

        redemption.CancelCollection(paid.Id, "iade", Now.AddMonths(2)).IsSuccess.Should().BeTrue();
        paid.IsClawback.Should().BeTrue();

        redemption.CancelCollection(paid.Id, "iade", Now).Error.Code.Should().Be("DiscountRedemptions.CollectionAlreadyCancelled");
        redemption.MarkCommissionPaid(accrued.Id, Now, null).Error.Code.Should().Be("DiscountRedemptions.CommissionNotAccrued");
        redemption.MarkCommissionPaid(Guid.NewGuid(), Now, null).Error.Should().Be(DiscountRedemption.CollectionNotFound);
        redemption.HasActiveCollection.Should().BeFalse();
    }

    [Fact]
    public void Partner_IsSelf_Should_Match_Linked_Tenant_Or_Normalized_Email()
    {
        var tenant = Guid.NewGuid();
        var partner = SalesPartner.Create(" Ali ", " Ali@Example.com ", " 0555 ", tenant, Now);

        partner.Name.Should().Be("Ali");
        partner.NormalizedEmail.Should().Be("ali@example.com");
        partner.IsSelf(tenant, null).Should().BeTrue();
        partner.IsSelf(Guid.NewGuid(), "ALI@example.com").Should().BeTrue();
        partner.IsSelf(Guid.NewGuid(), "baska@example.com").Should().BeFalse();
    }
}
